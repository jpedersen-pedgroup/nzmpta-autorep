// The offline Tester wizard (Preact). Reads/writes a LocalTest in IndexedDB, renders the
// resolver-driven step rail, and re-resolves live as the Machine Configuration changes — no
// server round-trip. This is the offline replacement for the server-rendered Wizard page.
import { render } from "preact";
import { useEffect, useState } from "preact/hooks";
import { resolveWizard } from "./wizardStepResolver";
import {
  defaultMachineConfiguration,
  type ChecklistAttestation,
  type MachineConfiguration,
  type VisualFaultEntry,
  type WizardStep,
} from "./types";
import { allTests, getTest, putTest, type FarmSnapshot, type LocalTest } from "../db/testStore";
import { fetchFarm } from "../farms";
import { useServerOnline } from "../connectivity";
import { MachineConfigStep } from "./MachineConfigStep";
import { ReadingsStep } from "./ReadingsStep";
import {
  additionalTestSections,
  testRecordSections,
} from "../passfail/standards";
import { FaultSummaryStep } from "./FaultSummaryStep";
import { buildFaultInputs } from "../faults/buildFaults";
import { VisualFaultsStep } from "./VisualFaultsStep";
import { PulsatorStep } from "./PulsatorStep";
import { ClusterStep } from "./ClusterStep";
import { ReviewSignOffStep } from "./ReviewSignOffStep";
import { downloadTestSummaryPdf } from "../report/testSummaryPdf";
import { adaptLegacyReadings } from "../report/legacyAdapter";
import { syncAll } from "../sync/syncClient";
import { showToast } from "../ui/toast";
import { DatePicker } from "../ui/DatePicker";
import {
  applyCheckAll,
  checklistComplete,
  preStartSections,
  runningSectionsFor,
  type ChecklistSection,
} from "./visualChecklist";

const ATTESTATION_TEXT =
  "I have inspected all items in this section and confirm they have been seen, tested and are in order.";
const SIGN_OFF_ATTEST = "I confirm this test has been completed and the results are accurate.";

export interface WizardOptions {
  id?: string;
  farmId?: string;
  farmName?: string;
  /** Admin read-only view: fetch this test from the server (not IndexedDB) and render it read-only. */
  serverTestId?: string;
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
}

/** Build a read-only LocalTest from a server fetch (admin view). Migrated legacy payloads are
 * adapted to readings + as-recorded verdicts; new-format payloads rehydrate directly. */
function localTestFromServer(dto: ServerTestDto): LocalTest {
  const now = new Date().toISOString();
  const base: Partial<LocalTest> = {};
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
      } else {
        Object.assign(base, parsed as Partial<LocalTest>);
      }
    } catch {
      /* fall through to a minimal shell */
    }
  }
  return {
    id: dto.id,
    farmName: dto.farmName,
    config: dto.config ?? base.config ?? defaultMachineConfiguration(),
    currentStep: "Setup",
    visualFaults: base.visualFaults ?? {},
    attestations: base.attestations ?? [],
    readings: base.readings ?? {},
    verdicts: base.verdicts,
    recommendations: base.recommendations ?? {},
    dataFields: base.dataFields ?? {},
    pulsatorRows: base.pulsatorRows,
    clusterRows: base.clusterRows,
    notes: base.notes,
    recordedRecommendations: base.recordedRecommendations,
    recordedVisualFaults: base.recordedVisualFaults,
    createdAt: dto.createdAt,
    updatedAt: now,
    markedCompleteAt: dto.markedCompleteAt,
    syncState: "uploaded",
    everUploaded: true,
    readonly: true,
    version: typeof base.version === "number" ? base.version : 1,
    supersedesId: typeof base.supersedesId === "string" ? base.supersedesId : undefined,
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

function computeCompleted(t: LocalTest): Set<WizardStep> {
  const done = new Set<WizardStep>();
  if (t.farmName.trim().length > 0) done.add("Setup");
  if (t.config.clusterCount > 0) done.add("MachineConfiguration");
  if (checklistComplete(preStartSections(t.config.hasReleaserPump), t.visualFaults)) {
    done.add("VisualFaultsPreStart");
  }
  const runningKeys = resolveWizard(t.config).steps.find((s) => s.step === "VisualFaultsRunning")?.sections ?? [];
  if (checklistComplete(runningSectionsFor(runningKeys), t.visualFaults)) {
    done.add("VisualFaultsRunning");
  }
  if (testRecordSections(t.config, t.readings).every((s) => s.readings.every((r) => t.readings[r.key] != null))) {
    done.add("TestRecord");
  }
  if (additionalTestSections(t.config, t.readings).every((s) => s.readings.every((r) => t.readings[r.key] != null))) {
    done.add("AdditionalTests");
  }
  if ((t.pulsatorRows ?? []).length > 0) {
    done.add("PulsatorTest");
  }
  if ((t.clusterRows ?? []).length > 0) {
    done.add("IndividualClusterTest");
  }
  const faults = buildFaultInputs(t);
  if (faults.every((f) => f.key != null && (t.recommendations[f.key] ?? "").trim().length > 0)) {
    done.add("FaultSummary");
  }
  return done;
}

function WizardApp({ id, farmId, farmName, serverTestId }: WizardOptions) {
  const [test, setTest] = useState<LocalTest | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [syncing, setSyncing] = useState(false);
  const [generating, setGenerating] = useState(false);
  const online = useServerOnline();

  useEffect(() => {
    let active = true;
    void (async () => {
      // Admin read-only view: load the test from the server, never IndexedDB.
      if (serverTestId) {
        try {
          const res = await fetch(`/api/tests/${serverTestId}`, { headers: { Accept: "application/json" } });
          if (!res.ok) throw new Error(String(res.status));
          const dto = (await res.json()) as ServerTestDto;
          if (active) setTest(localTestFromServer(dto));
        } catch {
          if (active) setError("This test could not be loaded.");
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
      if (t.farmId && !t.farm) {
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
  }, [id, farmId, farmName, serverTestId]);

  if (error) return <div class="card">{error}</div>;
  if (!test) return <div class="card">Loading test…</div>;

  const persist = async (patch: Partial<LocalTest>) => {
    const updated: LocalTest = { ...test, ...patch, updatedAt: new Date().toISOString() };
    setTest(updated);
    // Admin view mode is in-memory only — never write another tester's test into this device's store.
    if (!serverTestId) await putTest(updated);
  };
  // Migrated legacy tests are read-only: navigation still persists (currentStep), but data edits
  // are no-ops so a historical record can't be altered on-device.
  const readonly = test.readonly ?? false;
  const persistEdit = (patch: Partial<LocalTest>) => (readonly ? Promise.resolve() : persist(patch));
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
      showToast(`${msg} — synced (${r.pushed} pushed, ${r.pulled} pulled).`, "success");
    } catch {
      showToast(`${msg} — saved; will sync when back online.`, "info");
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
    await persist({
      markedCompleteAt: now,
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
  const idx = plan.steps.findIndex((s) => s.step === current);
  const prev = idx > 0 ? plan.steps[idx - 1].step : null;
  const next = idx >= 0 && idx < plan.steps.length - 1 ? plan.steps[idx + 1].step : null;
  const currentStep = plan.steps[idx] ?? plan.steps[0];

  const preStart = preStartSections(test.config.hasReleaserPump);
  const running = runningSectionsFor(currentStep.sections);
  const attestedSectionsFor = (step: WizardStep) =>
    test.attestations.filter((a) => a.step === step && a.section).map((a) => a.section!);

  return (
    <div>
      <div class="page-header page-header--wizard">
        <div class="page-header__heading">
          <h1>{test.farmName || "New machine test"}</h1>
          <p>
            <span class={online ? "badge badge--success" : "badge badge--warning"}>
              {online ? "Online" : "Offline — saved on device"}
            </span>
            {(test.version ?? 1) > 1 && <> <span class="badge">Version {test.version}</span></>}
          </p>
        </div>
        <div class="page-header__actions">
          <a class="btn btn--danger-soft btn--sm" href={serverTestId ? "/Admin/Tests" : "/App/Tests/Index"}>Exit</a>
        </div>
      </div>

      {plan.isShortTest && (
        <div class="alert alert--warning">
          ⚠️ <strong>Short test</strong> — ISO ports unavailable, so only the essential tests are required.
        </div>
      )}

      {readonly && (
        <div class="alert alert--warning">
          📄 <strong>Read-only</strong> — this test can't be edited here. Pass/fail is shown as
          recorded at the time of testing.
        </div>
      )}

      <div class="wizard">
        <nav class="wizard__rail">
          {plan.steps.map((s, i) => (
            <a
              key={s.step}
              class={
                "wizard__step" +
                (s.step === current ? " is-current" : "") +
                (completed.has(s.step) ? " is-complete" : "")
              }
              href="#"
              onClick={(e) => {
                e.preventDefault();
                void go(s.step);
              }}
            >
              <span class="wizard__num">{i + 1}</span>
              <span class="wizard__step-labels">
                <span class="wizard__step-title">{s.title}</span>
                {s.isOptional && <span class="wizard__opt">optional</span>}
              </span>
            </a>
          ))}
        </nav>

        <div class="wizard__content">
          <div class="wizard__panel" key={current}>
          {current === "Setup" && (
            <div class="card">
              <div class="card__title">Farm &amp; details</div>
              <div class="form-grid">
                {farmField("Farm", test.farm?.name ?? test.farmName)}
                {farmField("Supply number", test.farm?.supplyNumber)}
                {farmField("Milk supply company", test.farm?.milkCompanyName)}
                {farmField("Region", test.farm?.regionName)}
                {farmField("Address", farmAddress(test.farm))}
                {farmField("RAPID number", test.farm?.rapidNumber)}
                {farmField("Farmer", test.farm?.farmerName)}
                {farmField("Phone", test.farm?.contactPhone)}
                {farmField("Email", test.farm?.contactEmail)}
              </div>
              <p style="color:var(--text-muted);font-size:0.8125rem;margin-top:var(--space-4)">
                Farm details are managed in the admin area.
              </p>

              <div class="card__title" style="margin-top:var(--space-5)">
                Calibration expiry dates{" "}
                <small class="card__hint">Your test equipment — air-flow meters, pulsator testers, vacuum gauges.</small>
              </div>
              <div class="form-grid">
                {calDateField("Air-flow meters", test.calAirFlowMeters, (v) => void persistEdit({ calAirFlowMeters: v }))}
                {calDateField("Pulsator testers", test.calPulsatorTesters, (v) => void persistEdit({ calPulsatorTesters: v }))}
                {calDateField("Vacuum gauges", test.calVacuumGauges, (v) => void persistEdit({ calVacuumGauges: v }))}
              </div>
            </div>
          )}

          {current === "MachineConfiguration" && (
            <MachineConfigStep config={test.config} onChange={(patch) => void setConfig(patch)} />
          )}

          {current === "VisualFaultsPreStart" && (
            <VisualFaultsStep
              title="Visual faults — pre-start"
              sections={preStart}
              entries={test.visualFaults}
              onSetEntry={(k, e) => void setVisualFault(k, e)}
              onCheckAll={(secKey) => {
                const sec = preStart.find((s) => s.key === secKey);
                if (sec) void checkAllSection("VisualFaultsPreStart", sec);
              }}
              attestedSections={attestedSectionsFor("VisualFaultsPreStart")}
              dataValues={test.dataFields ?? {}}
              onSetData={(k, v) => void setDataField(k, v)}
            />
          )}

          {current === "VisualFaultsRunning" && (
            <VisualFaultsStep
              title="Visual faults — running"
              sections={running}
              entries={test.visualFaults}
              onSetEntry={(k, e) => void setVisualFault(k, e)}
              onCheckAll={(secKey) => {
                const sec = running.find((s) => s.key === secKey);
                if (sec) void checkAllSection("VisualFaultsRunning", sec);
              }}
              attestedSections={attestedSectionsFor("VisualFaultsRunning")}
              dataValues={test.dataFields ?? {}}
              onSetData={(k, v) => void setDataField(k, v)}
              guards={{ value: test.guardsOnPulsators ?? false, onChange: (v) => void persist({ guardsOnPulsators: v }) }}
            />
          )}

          {current === "TestRecord" && (
            <ReadingsStep
              title="Test Record"
              hint="Enter readings — pass/fail is live against the standard for this machine."
              sections={testRecordSections(test.config, test.readings)}
              readings={test.readings}
              onSetReading={(k, v) => void setReading(k, v)}
              readonly={readonly}
              storedVerdicts={test.verdicts}
            />
          )}

          {current === "AdditionalTests" && (
            <ReadingsStep
              title="Additional Tests"
              hint="Only the sections relevant to this machine's ancillaries are shown."
              sections={additionalTestSections(test.config, test.readings)}
              readings={test.readings}
              onSetReading={(k, v) => void setReading(k, v)}
              readonly={readonly}
              storedVerdicts={test.verdicts}
            />
          )}

          {current === "PulsatorTest" && (
            <PulsatorStep
              config={test.config}
              rows={test.pulsatorRows ?? []}
              onRows={(rows) => void persistEdit({ pulsatorRows: rows })}
              readings={test.readings}
              onSetReading={(k, v) => void setReading(k, v)}
              readonly={readonly}
              storedVerdicts={test.verdicts}
            />
          )}

          {current === "IndividualClusterTest" && (
            <ClusterStep
              config={test.config}
              rows={test.clusterRows ?? []}
              onRows={(rows) => void persistEdit({ clusterRows: rows })}
              readonly={readonly}
            />
          )}

          {current === "FaultSummary" && (
            <FaultSummaryStep test={test} onSetRecommendation={(k, v) => void setRecommendation(k, v)} />
          )}

          {current === "ReviewSignOff" && (
            <ReviewSignOffStep
              test={test}
              steps={plan.steps}
              completed={completed}
              syncing={syncing}
              generating={generating}
              onMarkComplete={() => void markComplete()}
              onResync={() => void runSync("Re-synced")}
              onDownloadReport={() => {
                setGenerating(true);
                void downloadTestSummaryPdf(test)
                  .catch(() => showToast("Could not generate the report on this device.", "error"))
                  .finally(() => setGenerating(false));
              }}
              onAttachPdf={(file) => void attachPulsationPdf(file)}
              onRemovePdf={() => void persistEdit({ pulsationPdf: null, syncState: "local-only" })}
            />
          )}

          {current !== "Setup" &&
            current !== "MachineConfiguration" &&
            current !== "VisualFaultsPreStart" &&
            current !== "VisualFaultsRunning" &&
            current !== "TestRecord" &&
            current !== "AdditionalTests" &&
            current !== "PulsatorTest" &&
            current !== "IndividualClusterTest" &&
            current !== "FaultSummary" &&
            current !== "ReviewSignOff" && (
              <div class="card">
                <div class="card__title">
                  {currentStep.title} <small class="card__hint">Offline data entry for this step is coming next.</small>
                </div>
                {currentStep.sections.length > 0 && (
                  <ul style="margin-top:var(--space-2)">
                    {currentStep.sections.map((sec) => (
                      <li key={sec} style="padding:2px 0;font-size:0.9rem">• {sec}</li>
                    ))}
                  </ul>
                )}
              </div>
            )}
          </div>

          <div class="wizard__nav">
            <div>{prev && <button class="btn btn--secondary" onClick={() => void go(prev)}>‹ Back</button>}</div>
            <div>{next && <button class="btn" onClick={() => void go(next)}>Next ›</button>}</div>
          </div>
        </div>
      </div>
    </div>
  );
}

function farmField(label: string, value?: string | null) {
  return (
    <div>
      <span style="color:var(--text-muted);font-size:0.8125rem">{label}</span>
      <div>{value && value.length > 0 ? value : "—"}</div>
    </div>
  );
}

function calDateField(label: string, value: string | null | undefined, onChange: (v: string | null) => void) {
  return (
    <div class="form-field">
      <label>{label}</label>
      <DatePicker value={value} onChange={onChange} />
    </div>
  );
}

function farmAddress(f?: FarmSnapshot): string | null {
  if (!f) return null;
  const parts = [f.addressLine1, f.addressLine2, f.town, f.postCode].filter((p): p is string => Boolean(p));
  return parts.length > 0 ? parts.join(", ") : null;
}
