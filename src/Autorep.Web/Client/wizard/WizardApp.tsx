// The offline Tester wizard (Preact). Reads/writes a LocalTest in IndexedDB and re-resolves the
// step plan live as the Machine Configuration changes — no server round-trip. This is the offline
// replacement for the server-rendered Wizard page.
//
// This module owns the test and every handler that mutates it; the chrome around it belongs to a
// shell (see ./shells) chosen by the tester from the header cog. Shells arrange steps, they never
// collect data — so switching layout mid-test can't change what's recorded.
import { render, type VNode } from "preact";
import { useEffect, useState } from "preact/hooks";
import { resolveWizard } from "./wizardStepResolver";
import {
  defaultMachineConfiguration,
  type ChecklistAttestation,
  type MachineConfiguration,
  type VisualFaultEntry,
  type WizardStep,
} from "./types";
import { allTests, getTest, putTest, type LocalTest } from "../db/testStore";
import { fetchFarm } from "../farms";
import { buildAmendmentRecord } from "../versioning/amendments";
import { useServerOnline } from "../connectivity";
import { downloadTestSummaryPdf } from "../report/testSummaryPdf";
import { ReportGeneratorUnavailableError } from "../report/generatorChunks";
import { adaptLegacyReadings } from "../report/legacyAdapter";
import { syncAll, SessionExpiredError } from "../sync/syncClient";
import { getCachedCalibration } from "../sync/calibrationSync";
import type { CalibrationDates } from "../calibration/status";
import { CalibrationAlert } from "../ui/CalibrationPanel";
import { LayoutMenu } from "../ui/LayoutMenu";
import { showToast } from "../ui/toast";
import { applyCheckAll, type ChecklistSection } from "./visualChecklist";
import { computeCompleted } from "./wizardProgress";
import type { StepContext } from "./WizardSteps";
import { getLayout, setLayout } from "./layoutPreference";
import { RailShell } from "./shells/RailShell";
import { ScrollShell } from "./shells/ScrollShell";
import { HubShell } from "./shells/HubShell";
import { DEFAULT_LAYOUT, type ShellProps, type WizardLayout } from "./shells/types";

const ATTESTATION_TEXT =
  "I have inspected all items in this section and confirm they have been seen, tested and are in order.";
const SIGN_OFF_ATTEST = "I confirm this test has been completed and the results are accurate.";

export interface WizardOptions {
  id?: string;
  farmId?: string;
  farmName?: string;
  /** Read-only server view (admin "view any test", or a tester reading a company colleague's
   * test): fetch this test from the server, not IndexedDB, and never write it to this device. */
  serverTestId?: string;
  /** Where "Back" returns to from a server view — the admin list or the Company tests list. */
  backHref?: string;
}

export function mountWizard(root: HTMLElement, opts: WizardOptions): void {
  render(<WizardApp {...opts} />, root);
}

interface ServerTestDto {
  id: string;
  farmName: string;
  createdAt: string;
  markedCompleteAt: string | null;
  config: MachineConfiguration | null;
  payloadJson: string | null;
  testerName: string | null;
  isMine: boolean;
}

/** Build a read-only LocalTest from a server fetch. Migrated legacy payloads are adapted to
 * readings + as-recorded verdicts; new-format payloads rehydrate wholesale — the payload IS a
 * serialised LocalTest, so it is spread rather than field-listed (the same rehydration the sync
 * pull does). Field-listing here previously dropped the farm snapshot, the attached pulsation PDF
 * and the calibration dates, so the view showed "—" for the farm block and the report lost its
 * analyser pages. Server-authoritative fields are then overridden on top. */
function localTestFromServer(dto: ServerTestDto): LocalTest {
  const now = new Date().toISOString();
  let base: Partial<LocalTest> = {};
  if (dto.payloadJson) {
    try {
      const parsed = JSON.parse(dto.payloadJson) as Record<string, unknown>;
      if (parsed.legacy !== undefined && parsed.currentStep === undefined) {
        const adapted = adaptLegacyReadings(parsed);
        base.readings = adapted.readings;
        base.verdicts = adapted.verdicts;
        base.notes = adapted.comment;
        base.recordedRecommendations = adapted.recordedRecommendations;
        base.recordedVisualFaults = adapted.recordedVisualFaults;
        base.clusterRows = adapted.clusterRows;
        base.calAirFlowMeters = adapted.calAirFlowMeters;
        base.calPulsatorTesters = adapted.calPulsatorTesters;
        base.calVacuumGauges = adapted.calVacuumGauges;
      } else {
        base = parsed as unknown as Partial<LocalTest>;
      }
    } catch {
      /* fall through to a minimal shell */
    }
  }
  return {
    ...base,
    id: dto.id,
    farmName: dto.farmName || base.farmName || "",
    config: dto.config ?? base.config ?? defaultMachineConfiguration(),
    currentStep: "Setup",
    visualFaults: base.visualFaults ?? {},
    attestations: base.attestations ?? [],
    readings: base.readings ?? {},
    recommendations: base.recommendations ?? {},
    dataFields: base.dataFields ?? {},
    createdAt: dto.createdAt,
    updatedAt: now,
    markedCompleteAt: dto.markedCompleteAt,
    syncState: "uploaded",
    everUploaded: true,
    readonly: true,
    version: typeof base.version === "number" ? base.version : 1,
  };
}

function newLocalTest(farmId?: string, farmName?: string): LocalTest {
  const now = new Date().toISOString();
  return {
    id: crypto.randomUUID(),
    farmId: farmId ?? null,
    farmName: farmName ?? "",
    config: defaultMachineConfiguration(),
    currentStep: "Setup",
    visualFaults: {},
    attestations: [],
    readings: {},
    recommendations: {},
    dataFields: {},
    createdAt: now,
    updatedAt: now,
    markedCompleteAt: null,
    syncState: "local-only",
    version: 1,
  };
}

/** Why a server-fetched test isn't on screen. Distinguishing these matters in the field: "you're
 * offline" is recoverable and the tester should wait, "not found" never will be. */
type LoadFailure = "offline" | "notfound" | "failed";

const SHELLS: Record<WizardLayout, (props: ShellProps) => VNode> = {
  rail: RailShell,
  scroll: ScrollShell,
  hub: HubShell,
};

function WizardApp({ id, farmId, farmName, serverTestId, backHref }: WizardOptions) {
  const [test, setTest] = useState<LocalTest | null>(null);
  const [error, setError] = useState<LoadFailure | null>(null);
  const [colleagueName, setColleagueName] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);
  const [syncing, setSyncing] = useState(false);
  const [generating, setGenerating] = useState(false);
  // The tester's own equipment calibration dates (profile data, cached on-device) — drives the
  // renewal banner while testing. Not loaded on the admin read-only view (no tester profile).
  const [calDates, setCalDates] = useState<CalibrationDates | null>(null);
  // Read once — the preference only changes through the header cog, which sets both at once.
  const [layout, setLayoutState] = useState<WizardLayout>(() => getLayout());
  const online = useServerOnline();

  // The scroll and hub layouts bring their own header, so the standard app header (rendered by
  // _Layout, outside this mount root) has to stand down. A body class is the only lever that
  // reaches it, and it keeps switching instant rather than needing a page load.
  useEffect(() => {
    const chromeless = layout !== "rail";
    document.body.classList.toggle("wizard-chromeless", chromeless);
    return () => document.body.classList.remove("wizard-chromeless");
  }, [layout]);

  useEffect(() => {
    if (serverTestId) return;
    let active = true;
    void getCachedCalibration().then((c) => {
      if (active) setCalDates(c);
    });
    return () => {
      active = false;
    };
  }, [serverTestId]);

  useEffect(() => {
    let active = true;
    void (async () => {
      // Read-only server view: load the test over the network, never IndexedDB.
      if (serverTestId) {
        try {
          setError(null);
          const res = await fetch(`/api/tests/${serverTestId}`, { headers: { Accept: "application/json" } });
          // 404 covers both "gone" and "not yours" — the API deliberately doesn't distinguish, so
          // neither does this message.
          if (res.status === 404) throw new Error("notfound");
          if (!res.ok) throw new Error("failed");
          const dto = (await res.json()) as ServerTestDto;
          if (active) {
            setTest(localTestFromServer(dto));
            // Only a colleague's name is worth surfacing — naming yourself on your own test is
            // noise, and would word the read-only banner as if someone else owned it.
            setColleagueName(dto.isMine ? null : dto.testerName);
          }
        } catch (e) {
          if (!active) return;
          const reason = e instanceof Error ? e.message : "";
          setError(reason === "notfound" ? "notfound" : navigator.onLine ? "failed" : "offline");
        }
        return;
      }

      let t = id ? await getTest(id) : undefined;
      if (!t) {
        t = newLocalTest(farmId, farmName);
        await putTest(t);
        const url = new URL(location.href);
        url.search = "";
        url.searchParams.set("id", t.id);
        history.replaceState(null, "", url.toString());
      }
      // Best-effort: load full farm details (online) and snapshot them for display + sync.
      // Editable tests only — a completed version is frozen (its snapshot is part of the record
      // the amendment diff runs against), so never backfill into one.
      if (t.farmId && !t.farm && !t.markedCompleteAt && !t.readonly) {
        const snap = await fetchFarm(t.farmId);
        if (snap) {
          t = { ...t, farm: snap, farmName: snap.name };
          await putTest(t);
        }
      }
      // A superseded original is read-only even if its stored flag didn't round-trip from another
      // device — having any later version supersede it is the source of truth.
      if (!t.readonly && (await allTests()).some((x) => x.supersedesId === t!.id)) {
        t = { ...t, readonly: true };
      }
      if (active) setTest(t);
    })();
    return () => {
      active = false;
    };
  }, [id, farmId, farmName, serverTestId, reloadKey]);

  if (error) {
    const back = backHref ?? "/App/Tests/Index";
    return (
      <div class="card empty">
        {error === "notfound" ? (
          <>
            <h2>You can't open this test</h2>
            <p>It may have been removed, or it belongs to a tester outside your company.</p>
          </>
        ) : error === "offline" ? (
          <>
            <h2>This test isn't on your device</h2>
            <p>
              Company tests are read from the server, so you'll need a connection to open this one.
              Nothing has been lost — try again once you have signal.
            </p>
          </>
        ) : (
          <>
            <h2>Couldn't load this test</h2>
            <p>The server didn't answer. Your connection may have dropped.</p>
          </>
        )}
        <div class="form-actions">
          {error !== "notfound" && (
            <button class="btn" onClick={() => setReloadKey((k) => k + 1)}>Try again</button>
          )}
          <a class="btn btn--secondary" href={back}>Back</a>
        </div>
      </div>
    );
  }
  if (!test) return <div class="card">Loading test…</div>;

  const persist = async (patch: Partial<LocalTest>) => {
    const updated: LocalTest = { ...test, ...patch, updatedAt: new Date().toISOString() };
    setTest(updated);
    // Admin view mode is in-memory only — never write another tester's test into this device's store.
    if (!serverTestId) await putTest(updated);
  };
  // Read-only: migrated legacy tests, superseded originals AND any completed test. Completion
  // freezes a version — without that, a signed-off test could be silently altered afterwards and
  // the next version's amendment diff would report a falsified "Previous" column. Changing a
  // completed test is done by reopening it as a new version (the audited path). Navigation still
  // persists (currentStep), but data edits are no-ops.
  const readonly = (test.readonly ?? false) || test.markedCompleteAt != null;
  // Every data edit flips the test dirty ("local-only") so it re-pushes on the next sync —
  // an edit that kept syncState "uploaded" would silently diverge from the server copy.
  const persistEdit = (patch: Partial<LocalTest>) =>
    readonly ? Promise.resolve() : persist({ syncState: "local-only", ...patch });
  const setConfig = (patch: Partial<MachineConfiguration>) =>
    persistEdit({ config: { ...test.config, ...patch } });
  const go = (step: WizardStep) => persist({ currentStep: step });

  const setVisualFault = (key: string, entry: VisualFaultEntry | null) => {
    const visualFaults = { ...test.visualFaults };
    if (entry) visualFaults[key] = entry;
    else delete visualFaults[key];
    return persistEdit({ visualFaults });
  };
  const setReading = (key: string, value: number | null) => {
    const readings = { ...test.readings };
    if (value === null || Number.isNaN(value)) delete readings[key];
    else readings[key] = value;
    return persistEdit({ readings });
  };
  const setRecommendation = (key: string, value: string) => {
    const recommendations = { ...test.recommendations };
    if (value.trim() === "") delete recommendations[key];
    else recommendations[key] = value;
    return persistEdit({ recommendations });
  };
  const setDataField = (key: string, value: string) => {
    const dataFields = { ...(test.dataFields ?? {}) };
    if (value.trim() === "") delete dataFields[key];
    else dataFields[key] = value;
    return persistEdit({ dataFields });
  };
  const checkAllSection = (step: WizardStep, section: ChecklistSection) => {
    const attestation: ChecklistAttestation = {
      step,
      section: section.key,
      attestedAt: new Date().toISOString(),
      text: ATTESTATION_TEXT,
    };
    return persistEdit({
      visualFaults: applyCheckAll([section], test.visualFaults),
      attestations: [...test.attestations, attestation],
    });
  };
  const runSync = async (msg: string) => {
    setSyncing(true);
    try {
      const r = await syncAll();
      const fresh = await getTest(test.id);
      if (fresh) setTest(fresh);
      if (fresh && fresh.syncState === "local-only") {
        // This test is still unsent even though the sync ran — don't claim it synced.
        showToast(`${msg} — saved here, but it couldn't be sent. It'll retry on the next sync.`, "error");
      } else {
        showToast(`${msg} — synced (${r.pushed} pushed, ${r.pulled} pulled).`, "success");
      }
    } catch (e) {
      showToast(
        e instanceof SessionExpiredError
          ? `${msg} — saved here, but you've been signed out. Sign in again to sync.`
          : `${msg} — saved; will sync when back online.`,
        e instanceof SessionExpiredError ? "error" : "info",
      );
    } finally {
      setSyncing(false);
    }
  };
  const attachPulsationPdf = async (file: File) => {
    if (readonly) return;
    const isPdf = file.type === "application/pdf" || /\.pdf$/i.test(file.name);
    if (!isPdf) {
      showToast("Only PDF files can be attached here.", "error");
      return;
    }
    if (file.size > 15 * 1024 * 1024) {
      showToast("That PDF is too large to attach (max 15 MB).", "error");
      return;
    }
    const base64 = await new Promise<string>((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => resolve(String(reader.result).split(",")[1] ?? "");
      reader.onerror = () => reject(reader.error);
      reader.readAsDataURL(file);
    });
    await persist({
      pulsationPdf: { name: file.name, base64, size: file.size, attachedAt: new Date().toISOString() },
      syncState: "local-only", // dirty — re-push carries the attachment
    });
    showToast(`Attached ${file.name} — it will be appended to the report.`, "success");
  };

  const markComplete = async () => {
    if (readonly) return;
    const now = new Date().toISOString();

    // Stamp the tester's CURRENT profile calibration dates into the test record at sign-off —
    // the report's frozen snapshot of equipment state at test time. A superseding version keeps
    // the dates carried from the version it amends (the original test date's truth), and a
    // missing profile value never blanks a previously stamped/entered one.
    let calStamp: Partial<LocalTest> = {};
    if (!test.supersedesId) {
      const cal = await getCachedCalibration();
      calStamp = {
        calAirFlowMeters: cal.airFlowMeters ?? test.calAirFlowMeters ?? null,
        calPulsatorTesters: cal.pulsatorTesters ?? test.calPulsatorTesters ?? null,
        calVacuumGauges: cal.vacuumGauges ?? test.calVacuumGauges ?? null,
      };
    }

    // Re-edit of a completed test: fix the amendment record (what changed vs the superseded
    // version, when, by whom) at sign-off, appended to the cumulative chain the copy carried
    // forward. Replaces any same-version record so a repeated sign-off can't double-log.
    let amendments = test.amendments;
    if (test.supersedesId) {
      const base = await getTest(test.supersedesId);
      const amendedBy = (globalThis as { __autorepTesterName?: unknown }).__autorepTesterName;
      const record = buildAmendmentRecord(
        base, test, now, typeof amendedBy === "string" ? amendedBy : undefined,
      );
      amendments = [...(test.amendments ?? []).filter((a) => a.version !== record.version), record];
    }

    await persist({
      markedCompleteAt: now,
      amendments,
      ...calStamp,
      syncState: "local-only",
      attestations: [
        ...test.attestations,
        { step: "ReviewSignOff", attestedAt: now, text: SIGN_OFF_ATTEST },
      ],
    });
    await runSync("Test marked complete");
  };

  const plan = resolveWizard(test.config);
  const current = test.currentStep as WizardStep;
  const completed = computeCompleted(test);

  // Everything a step body needs, built once and handed to whichever shell is active. Handlers are
  // the same ones the rail has always used — a layout can reorder steps on screen but not change
  // what any of them writes.
  const ctx: StepContext = {
    test,
    readonly,
    plan,
    completed,
    serverTestId,
    colleagueName,
    syncing,
    generating,
    calDates,
    onCalDatesChanged: setCalDates,
    setConfig: (patch) => void setConfig(patch),
    setVisualFault: (key, entry) => void setVisualFault(key, entry),
    setReading: (key, value) => void setReading(key, value),
    setRecommendation: (key, value) => void setRecommendation(key, value),
    setDataField: (key, value) => void setDataField(key, value),
    checkAllSection: (step, section) => void checkAllSection(step, section),
    persistEdit: (patch) => void persistEdit(patch),
    onMarkComplete: () => void markComplete(),
    onResync: () => void runSync("Re-synced"),
    onDownloadReport: () => {
      setGenerating(true);
      void downloadTestSummaryPdf(test)
        .catch((e) =>
          // A missing generator chunk is recoverable and the tester can act on it — don't bury it
          // under the generic message.
          showToast(
            e instanceof ReportGeneratorUnavailableError
              ? e.message
              : "Could not generate the report on this device.",
            "error",
          ),
        )
        .finally(() => setGenerating(false));
    },
    onAttachPdf: (file) => void attachPulsationPdf(file),
    onRemovePdf: () => void persistEdit({ pulsationPdf: null, syncState: "local-only" }),
  };

  const banners = (
    <>
      {plan.isShortTest && (
        <div class="alert alert--warning">
          ⚠️ <strong>Short test</strong> — ISO ports unavailable, so only the essential tests are required.
        </div>
      )}

      {/* Someone else's test is neutral news — nothing is wrong and nothing you expected to do is
          blocked — so it reads as info, not a warning. Your own frozen test keeps the warning. */}
      {readonly &&
        (serverTestId && colleagueName ? (
          <div class="alert alert--info">
            👁 <strong>{colleagueName}'s test</strong> — read-only. You're viewing your company's test
            history; only {colleagueName.split(" ")[0]} can edit this test. Pass/fail is shown as
            recorded at the time of testing.
          </div>
        ) : (
          <div class="alert alert--warning">
            📄 <strong>Read-only</strong> — this test can't be edited here. Pass/fail is shown as
            recorded at the time of testing.
          </div>
        ))}

      {/* Renewal warning for the tester's own equipment while testing — informational only, never a
          gate. The Setup step shows it inside the calibration panel instead. */}
      {!serverTestId && !readonly && calDates && current !== "Setup" && <CalibrationAlert dates={calDates} />}
    </>
  );

  const Shell = SHELLS[layout] ?? SHELLS[DEFAULT_LAYOUT];
  return (
    <Shell
      ctx={ctx}
      current={current}
      onGo={(step) => void go(step)}
      online={online}
      layoutMenu={
        <LayoutMenu
          value={layout}
          onChange={(next) => {
            setLayout(next);
            setLayoutState(next);
          }}
        />
      }
      banners={banners}
      // "saved on device" would be a lie for a server view — this test isn't stored here.
      connectionLabel={
        online ? "Online" : serverTestId ? "Offline — this test isn't saved here" : "Offline — saved on device"
      }
      exitHref={serverTestId ? backHref ?? "/Admin/Tests" : "/App/Tests/Index"}
      exitLabel={serverTestId ? "Back" : "Exit"}
    />
  );
}
