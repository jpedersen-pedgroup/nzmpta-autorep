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
  type PlantType,
  type PumpLubrication,
  type VisualFaultEntry,
  type WizardStep,
} from "./types";
import { getTest, putTest, type FarmSnapshot, type LocalTest } from "../db/testStore";
import { fetchFarm } from "../farms";
import { Tabs } from "../ui/Tabs";
import { useServerOnline } from "../connectivity";
import { ReadingsStep } from "./ReadingsStep";
import {
  additionalTestSections,
  individualClusterSections,
  pulsatorSections,
  testRecordSections,
} from "../passfail/standards";
import { FaultSummaryStep } from "./FaultSummaryStep";
import { buildFaultInputs } from "../faults/buildFaults";
import { VisualFaultsStep } from "./VisualFaultsStep";
import {
  applyCheckAll,
  checklistComplete,
  preStartSections,
  runningSectionsFor,
  type ChecklistSection,
} from "./visualChecklist";

const ATTESTATION_TEXT =
  "I have inspected all items in this section and confirm they have been seen, tested and are in order.";

export interface WizardOptions {
  id?: string;
  farmId?: string;
  farmName?: string;
}

export function mountWizard(root: HTMLElement, opts: WizardOptions): void {
  render(<WizardApp {...opts} />, root);
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
  if (testRecordSections(t.config).every((s) => s.readings.every((r) => t.readings[r.key] != null))) {
    done.add("TestRecord");
  }
  if (additionalTestSections(t.config).every((s) => s.readings.every((r) => t.readings[r.key] != null))) {
    done.add("AdditionalTests");
  }
  if (pulsatorSections(t.config).every((s) => s.readings.every((r) => t.readings[r.key] != null))) {
    done.add("PulsatorTest");
  }
  if (individualClusterSections(t.config).every((s) => s.readings.every((r) => t.readings[r.key] != null))) {
    done.add("IndividualClusterTest");
  }
  const faults = buildFaultInputs(t);
  if (faults.every((f) => f.key != null && (t.recommendations[f.key] ?? "").trim().length > 0)) {
    done.add("FaultSummary");
  }
  return done;
}

function WizardApp({ id, farmId, farmName }: WizardOptions) {
  const [test, setTest] = useState<LocalTest | null>(null);
  const online = useServerOnline();

  useEffect(() => {
    let active = true;
    void (async () => {
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
      if (active) setTest(t);
    })();
    return () => {
      active = false;
    };
  }, [id, farmId, farmName]);

  if (!test) return <div class="card">Loading test…</div>;

  const persist = async (patch: Partial<LocalTest>) => {
    const updated: LocalTest = { ...test, ...patch, updatedAt: new Date().toISOString() };
    setTest(updated);
    await putTest(updated);
  };
  const setConfig = (patch: Partial<MachineConfiguration>) =>
    persist({ config: { ...test.config, ...patch } });
  const go = (step: WizardStep) => persist({ currentStep: step });

  const setVisualFault = (key: string, entry: VisualFaultEntry | null) => {
    const visualFaults = { ...test.visualFaults };
    if (entry) visualFaults[key] = entry;
    else delete visualFaults[key];
    return persist({ visualFaults });
  };
  const setReading = (key: string, value: number | null) => {
    const readings = { ...test.readings };
    if (value === null || Number.isNaN(value)) delete readings[key];
    else readings[key] = value;
    return persist({ readings });
  };
  const setRecommendation = (key: string, value: string) => {
    const recommendations = { ...test.recommendations };
    if (value.trim() === "") delete recommendations[key];
    else recommendations[key] = value;
    return persist({ recommendations });
  };
  const setDataField = (key: string, value: string) => {
    const dataFields = { ...(test.dataFields ?? {}) };
    if (value.trim() === "") delete dataFields[key];
    else dataFields[key] = value;
    return persist({ dataFields });
  };
  const checkAllSection = (step: WizardStep, section: ChecklistSection) => {
    const attestation: ChecklistAttestation = {
      step,
      section: section.key,
      attestedAt: new Date().toISOString(),
      text: ATTESTATION_TEXT,
    };
    return persist({
      visualFaults: applyCheckAll([section], test.visualFaults),
      attestations: [...test.attestations, attestation],
    });
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
          </p>
        </div>
        <div class="page-header__actions">
          <a class="btn btn--danger-soft btn--sm" href="/App/Tests/Index">Exit</a>
        </div>
      </div>

      {plan.isShortTest && (
        <div class="alert alert--warning">
          ⚠️ <strong>Short test</strong> — ISO ports unavailable, so only the essential tests are required.
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
            </div>
          )}

          {current === "MachineConfiguration" && (
            <div class="card">
              <div class="card__title">
                Machine configuration{" "}
                <small class="card__hint">Changes save to this device immediately and re-resolve the steps on the left.</small>
              </div>
              <Tabs
                tabs={[
                  {
                    key: "machine",
                    label: "Machine",
                    content: (
                      <div class="form-grid">
                        <div class="form-field">
                          <label>Plant type</label>
                          <select
                            value={test.config.plantType}
                            onChange={(e) =>
                              void setConfig({ plantType: (e.currentTarget as HTMLSelectElement).value as PlantType })
                            }
                          >
                            <option value="HerringboneLowline">Herringbone (lowline)</option>
                            <option value="HerringboneHighline">Herringbone (highline)</option>
                            <option value="Rotary">Rotary</option>
                            <option value="Other">Other</option>
                          </select>
                        </div>
                        <div class="form-field">
                          <label>Cluster count</label>
                          <input
                            type="number"
                            min="0"
                            value={test.config.clusterCount}
                            onInput={(e) =>
                              void setConfig({ clusterCount: Number((e.currentTarget as HTMLInputElement).value) || 0 })
                            }
                          />
                        </div>
                        <div class="form-field">
                          <label>Pump lubrication</label>
                          <select
                            value={test.config.pumpLubrication}
                            onChange={(e) =>
                              void setConfig({
                                pumpLubrication: (e.currentTarget as HTMLSelectElement).value as PumpLubrication,
                              })
                            }
                          >
                            <option value="OilLubricated">Oil lubricated</option>
                            <option value="LiquidRing">Liquid ring</option>
                            <option value="Other">Other</option>
                          </select>
                        </div>
                      </div>
                    ),
                  },
                  {
                    key: "ancillary",
                    label: "Ancillary equipment",
                    content: (
                      <div class="form-grid">
                        {confToggle("Variable speed drive (VSD)", test.config.vsdFitted, (v) => void setConfig({ vsdFitted: v }))}
                        {confToggle("ISO test ports available", test.config.isoPortsAvailable, (v) => void setConfig({ isoPortsAvailable: v }))}
                        {confToggle("Flushing pulsation system", test.config.flushingPulsationSystem, (v) => void setConfig({ flushingPulsationSystem: v }))}
                        {confToggle("Vented liners", test.config.linerVented, (v) => void setConfig({ linerVented: v }))}
                        {confToggle("Automatic cluster removers (ACRs)", test.config.hasAcr, (v) => void setConfig({ hasAcr: v }))}
                        {confToggle("Bail gates", test.config.hasBailGates, (v) => void setConfig({ hasBailGates: v }))}
                        {confToggle("Milk meters", test.config.hasMilkMeters, (v) => void setConfig({ hasMilkMeters: v }))}
                        {confToggle("Teat sprayer", test.config.hasTeatSprayer, (v) => void setConfig({ hasTeatSprayer: v }))}
                        {confToggle("Backing gate", test.config.hasBackingGate, (v) => void setConfig({ hasBackingGate: v }))}
                        {confToggle("Releaser pump", test.config.hasReleaserPump, (v) => void setConfig({ hasReleaserPump: v }))}
                      </div>
                    ),
                  },
                ]}
              />
            </div>
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
              sections={testRecordSections(test.config)}
              readings={test.readings}
              onSetReading={(k, v) => void setReading(k, v)}
            />
          )}

          {current === "AdditionalTests" && (
            <ReadingsStep
              title="Additional Tests"
              hint="Only the sections relevant to this machine's ancillaries are shown."
              sections={additionalTestSections(test.config)}
              readings={test.readings}
              onSetReading={(k, v) => void setReading(k, v)}
            />
          )}

          {current === "PulsatorTest" && (
            <ReadingsStep
              title="Pulsator Test Results"
              hint="Summary rates & ratios (per-pulsator rows coming next)."
              sections={pulsatorSections(test.config)}
              readings={test.readings}
              onSetReading={(k, v) => void setReading(k, v)}
            />
          )}

          {current === "IndividualClusterTest" && (
            <ReadingsStep
              title="Individual Cluster Tests"
              hint="Optional — per-cluster airflow."
              sections={individualClusterSections(test.config)}
              readings={test.readings}
              onSetReading={(k, v) => void setReading(k, v)}
            />
          )}

          {current === "FaultSummary" && (
            <FaultSummaryStep test={test} onSetRecommendation={(k, v) => void setRecommendation(k, v)} />
          )}

          {current !== "Setup" &&
            current !== "MachineConfiguration" &&
            current !== "VisualFaultsPreStart" &&
            current !== "VisualFaultsRunning" &&
            current !== "TestRecord" &&
            current !== "AdditionalTests" &&
            current !== "PulsatorTest" &&
            current !== "IndividualClusterTest" &&
            current !== "FaultSummary" && (
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

function farmAddress(f?: FarmSnapshot): string | null {
  if (!f) return null;
  const parts = [f.addressLine1, f.addressLine2, f.town, f.postCode].filter((p): p is string => Boolean(p));
  return parts.length > 0 ? parts.join(", ") : null;
}

function confToggle(label: string, checked: boolean, onChange: (v: boolean) => void) {
  return (
    <label class="form-check">
      <input
        type="checkbox"
        checked={checked}
        onChange={(e) => onChange((e.currentTarget as HTMLInputElement).checked)}
      />
      <span>{label}</span>
    </label>
  );
}
