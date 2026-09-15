import { describe, it, expect } from "vitest";
import {
  clusterStepApplies,
  computeCompleted,
  currentStepFor,
  faultsInStep,
  overallProgress,
  runningSectionKeys,
  stepProgress,
  subsFor,
  visibleSteps,
} from "./wizardProgress";
import { applyCheckAll, preStartSections, runningSectionsFor } from "./visualChecklist";
import { additionalTestSections, airflowSections, pulsatorSections, testRecordSections } from "../passfail/standards";
import { buildFaultInputs } from "../faults/buildFaults";
import { resolveWizard } from "./wizardStepResolver";
import { defaultMachineConfiguration, type MachineConfiguration } from "./types";
import type { LocalTest } from "../db/testStore";

function makeTest(over: Partial<LocalTest> = {}, config: Partial<MachineConfiguration> = {}): LocalTest {
  return {
    id: "t1",
    farmName: "Tievoli Farms Ltd",
    config: { ...defaultMachineConfiguration(), clusterCount: 24, ...config },
    currentStep: "Setup",
    visualFaults: {},
    attestations: [],
    readings: {},
    recommendations: {},
    dataFields: {},
    createdAt: "2026-08-01T00:00:00.000Z",
    updatedAt: "2026-08-01T00:00:00.000Z",
    markedCompleteAt: null,
    syncState: "local-only",
    ...over,
  };
}

/** A test with every REQUIRED step complete. Individual Cluster Tests is deliberately left
 * untouched — it is the optional step these resume-target tests are about. */
function allRequiredDone(over: Partial<LocalTest> = {}): LocalTest {
  const config: MachineConfiguration = { ...defaultMachineConfiguration(), clusterCount: 24 };
  const checklists = [
    ...preStartSections(config.hasReleaserPump),
    ...runningSectionsFor(runningSectionKeys(config)),
  ];
  // Two passes: a few reading sections widen once other readings are present, so collect the keys
  // again against the filled map rather than assuming one pass sees them all.
  const keysFor = (readings: Record<string, number>) =>
    [
      ...testRecordSections(config, readings),
      ...airflowSections(config, readings),
      ...additionalTestSections(config, readings),
      ...pulsatorSections(config, readings),
    ].flatMap((s) => s.readings.map((r) => r.key));
  let readings = Object.fromEntries(keysFor({}).map((k) => [k, 42]));
  readings = Object.fromEntries(keysFor(readings).map((k) => [k, 42]));

  // No faulty-pulsator rows: a clean machine has none, and they don't gate the pulsation step.
  const base = makeTest(
    {
      visualFaults: applyCheckAll(checklists, {}),
      readings,
      ...over,
    },
    config,
  );
  // Readings that fail their standard become faults, and Fault Summary isn't done until every one
  // carries a recommendation — derive them rather than guessing which of the 42s fail.
  const recommendations = Object.fromEntries(
    buildFaultInputs(base).flatMap((f) => (f.key ? [[f.key, "Replace."] as const] : [])),
  );
  return { ...base, recommendations: { ...recommendations, ...(over.recommendations ?? {}) } };
}

describe("wizard progress", () => {
  it("asks the resolver which running sections apply to this machine", () => {
    const rotary = runningSectionKeys({ ...defaultMachineConfiguration(), plantType: "Rotary" });
    const bail = runningSectionKeys(defaultMachineConfiguration());
    expect(rotary).toContain("Rotaries");
    expect(bail).toContain("BailArea");
    expect(runningSectionKeys({ ...defaultMachineConfiguration(), hasAcr: true })).toContain("Acr");
  });

  it("scores a checklist step by the fraction of items answered", () => {
    const sections = preStartSections(false);
    const empty = makeTest();
    expect(stepProgress(empty, "VisualFaultsPreStart")).toBe(0);

    const full = makeTest({ visualFaults: applyCheckAll(sections, {}) });
    expect(stepProgress(full, "VisualFaultsPreStart")).toBe(1);

    const items = sections.flatMap((s) => s.items.filter((it) => !it.data));
    const half = makeTest({
      visualFaults: Object.fromEntries(
        items.slice(0, Math.floor(items.length / 2)).map((it) => [it.key, { status: "ok" as const }]),
      ),
    });
    const p = stepProgress(half, "VisualFaultsPreStart");
    expect(p).toBeGreaterThan(0);
    expect(p).toBeLessThan(1);
  });

  it("excludes data-capture fields from checklist progress", () => {
    // Data fields live in dataFields, never in visualFaults, so filling only those must not move
    // the bar — and answering every gating item must reach exactly 1 despite them existing.
    const sections = preStartSections(true);
    const dataItems = sections.flatMap((s) => s.items.filter((it) => it.data));
    expect(dataItems.length).toBeGreaterThan(0);

    const t = makeTest({ visualFaults: applyCheckAll(sections, {}) }, { hasReleaserPump: true });
    expect(stepProgress(t, "VisualFaultsPreStart")).toBe(1);
  });

  it("scores a readings step by the fraction of readings entered", () => {
    const config = { ...defaultMachineConfiguration(), clusterCount: 24 };
    const keys = testRecordSections(config).flatMap((s) => s.readings.map((r) => r.key));
    const half = keys.slice(0, Math.floor(keys.length / 2));
    const t = makeTest({ readings: Object.fromEntries(half.map((k) => [k, 42])) });
    const p = stepProgress(t, "TestRecord");
    expect(p).toBeGreaterThan(0);
    expect(p).toBeLessThan(1);
    expect(stepProgress(makeTest(), "TestRecord")).toBe(0);
  });

  it("treats a test with no faults as a finished Fault Summary", () => {
    // every() over an empty list is true, so computeCompleted calls it done — the fraction must
    // agree rather than showing an empty ring on a step with nothing to do.
    const t = makeTest();
    expect(stepProgress(t, "FaultSummary")).toBe(1);
    expect(computeCompleted(t).has("FaultSummary")).toBe(true);
  });

  it("counts logged faults per step", () => {
    const sections = preStartSections(false);
    const first = sections[0].items.filter((it) => !it.data)[0];
    const t = makeTest({
      visualFaults: { [first.key]: { status: "fault", severity: "Major" } },
    });
    expect(faultsInStep(t, "VisualFaultsPreStart")).toBe(1);
    expect(faultsInStep(t, "VisualFaultsRunning")).toBe(0);
    expect(faultsInStep(t, "Setup")).toBe(0);
  });

  it("measures overall progress over required steps only", () => {
    const empty = makeTest({ farmName: "" }, { clusterCount: 0 });
    expect(overallProgress(empty).pct).toBeLessThan(100);
    expect(overallProgress(empty).firstIncomplete?.step).toBe("Setup");

    // Individual Cluster Tests is optional, so leaving it out must not cap the bar below 100.
    const done = makeTest({
      visualFaults: applyCheckAll(
        [...preStartSections(false), ...runningSectionsFor(runningSectionKeys(defaultMachineConfiguration()))],
        {},
      ),
      readings: Object.fromEntries(
        [
          ...testRecordSections({ ...defaultMachineConfiguration(), clusterCount: 24 }),
        ].flatMap((s) => s.readings.map((r) => [r.key, 42] as const)),
      ),
      pulsatorRows: [],
    });
    expect(overallProgress(done).requiredCount).toBeGreaterThan(0);
    expect(overallProgress(done).pct).toBeGreaterThan(0);
  });

  it("completes the pulsation step from its ISO 14–15 readings, not from faulty-pulsator rows", () => {
    // Testers list only the pulsators that failed, so a clean machine has no rows — rows can't be
    // what finishes the step, or a good result would never read as done.
    const config = { ...defaultMachineConfiguration(), clusterCount: 24 };
    const keysFor = (readings: Record<string, number>) =>
      pulsatorSections(config, readings).flatMap((s) => s.readings.map((r) => r.key));
    let readings = Object.fromEntries(keysFor({}).map((k) => [k, 42]));
    readings = Object.fromEntries(keysFor(readings).map((k) => [k, 42]));
    expect(Object.keys(readings).length).toBeGreaterThan(0);

    const rowsOnly = makeTest({ pulsatorRows: [{ id: "p1", unit: "27", values: { rate: "60" } }] });
    expect(computeCompleted(rowsOnly).has("PulsatorTest")).toBe(false);
    expect(stepProgress(rowsOnly, "PulsatorTest")).toBe(0);

    const readingsDone = makeTest({ readings });
    expect(computeCompleted(readingsDone).has("PulsatorTest")).toBe(true);
    expect(stepProgress(readingsDone, "PulsatorTest")).toBe(1);
  });

  it("gates readings steps on the readings the tester types, never on calculated rows", () => {
    // A flushing plant with a milkline the cleaning reserve can't be worked out from: 2h stays
    // blank, and must not hold Test Record open for ever.
    const config = { ...defaultMachineConfiguration(), clusterCount: 24, flushingPulsationSystem: true, milklineSize: null };
    const typed = testRecordSections(config).flatMap((s) => s.readings.filter((r) => !r.derived).map((r) => r.key));
    const derived = testRecordSections(config).flatMap((s) => s.readings.filter((r) => r.derived).map((r) => r.key));
    expect(derived).toContain("tr.requiredCleaningReserve");
    const t = makeTest({ readings: Object.fromEntries(typed.map((k) => [k, 42])) }, config);
    expect(computeCompleted(t).has("TestRecord")).toBe(true);
    expect(stepProgress(t, "TestRecord")).toBe(1);
  });

  it("does not count an added-but-blank cluster row as a recorded result", () => {
    const blank = makeTest({ clusterRows: [{ id: "c0", unit: "", values: {} }] });
    expect(computeCompleted(blank).has("IndividualClusterTest")).toBe(false);
    expect(stepProgress(blank, "IndividualClusterTest")).toBe(0);
    const filled = makeTest({ clusterRows: [{ id: "c1", unit: "8", values: { totalAirAdmission: "40" } }] });
    expect(computeCompleted(filled).has("IndividualClusterTest")).toBe(true);
  });

  it("the fixture really does complete every required step", () => {
    // Guards the two tests below: if allRequiredDone() silently stopped completing something, they
    // would still pass while proving nothing about optional steps.
    const t = allRequiredDone();
    const done = computeCompleted(t);
    const required = resolveWizard(t.config).steps.filter((s) => !s.isOptional && s.step !== "ReviewSignOff");
    expect(required.filter((s) => !done.has(s.step)).map((s) => s.step)).toEqual([]);
    expect(done.has("IndividualClusterTest")).toBe(false);
  });

  it("stops offering a resume step once every required one is done", () => {
    // Individual Cluster Tests is optional and untouched, so it is not what the test still needs —
    // the shells read a null here as "ready to sign off".
    const t = allRequiredDone();
    const progress = overallProgress(t);
    expect(progress.pct).toBe(100);
    expect(progress.firstIncomplete).toBeNull();
  });

  it("skips an optional step to reach a required one further down the plan", () => {
    // Individual Cluster Tests sits AHEAD of Fault Summary, so a resume target that merely took the
    // first incomplete step would point at the untouched optional one and leave the real gap.
    const plan = resolveWizard({ ...defaultMachineConfiguration(), clusterCount: 24 });
    const order = plan.steps.map((s) => s.step);
    expect(order.indexOf("IndividualClusterTest")).toBeLessThan(order.indexOf("FaultSummary"));

    // A logged fault with no recommendation is what leaves Fault Summary incomplete.
    const faulted = preStartSections(false)[0].items.filter((i) => !i.data)[0];
    const t = allRequiredDone();
    const withGap: LocalTest = {
      ...t,
      visualFaults: { ...t.visualFaults, [faulted.key]: { status: "fault", severity: "Major" } },
      recommendations: Object.fromEntries(
        Object.entries(t.recommendations).filter(([k]) => k !== faulted.key),
      ),
    };
    expect(computeCompleted(withGap).has("FaultSummary")).toBe(false);
    expect(overallProgress(withGap).firstIncomplete?.step).toBe("FaultSummary");
  });

  it("shows Individual Cluster Tests only when cluster air admission (12b) fails, or rows exist", () => {
    // 20 clusters → 12b must sit in 80–240. Airflow readings alone don't bring the step in.
    const passing = makeTest({ readings: { "add.airflowMilkSystem": 4320, "add.clusterAirAdmissionConnect": 4100, "add.clusterAirAdmission": 220 } });
    expect(clusterStepApplies(passing)).toBe(false);
    expect(visibleSteps(passing).map((s) => s.step)).not.toContain("IndividualClusterTest");

    const failing = makeTest({ readings: { "add.airflowMilkSystem": 4320, "add.clusterAirAdmissionConnect": 3850, "add.clusterAirAdmission": 470 } });
    expect(clusterStepApplies(failing)).toBe(true);
    const order = visibleSteps(failing).map((s) => s.step);
    // …and it sits straight after the airflow step, as on the flowchart.
    expect(order.indexOf("IndividualClusterTest")).toBe(order.indexOf("AirflowTests") + 1);

    // Data already recorded is never hidden, whatever 12b now says.
    const withRows = makeTest({ clusterRows: [{ id: "c1", unit: "8", values: { totalAirAdmission: "40" } }] });
    expect(visibleSteps(withRows).map((s) => s.step)).toContain("IndividualClusterTest");
  });

  it("lands a persisted step that is no longer shown on the nearest earlier visible step", () => {
    const parked = makeTest({ currentStep: "IndividualClusterTest" }); // 12b now passes → hidden
    expect(currentStepFor(parked, visibleSteps(parked))).toBe("AirflowTests");
    const unknown = makeTest({ currentStep: "SomethingFromAnOlderBuild" });
    expect(currentStepFor(unknown, visibleSteps(unknown))).toBe("Setup");
    const fine = makeTest({ currentStep: "PulsatorTest" });
    expect(currentStepFor(fine, visibleSteps(fine))).toBe("PulsatorTest");
  });

  it("splits paginated steps into sub-sections and leaves the rest single", () => {
    const t = makeTest();
    expect(subsFor(t, "VisualFaultsPreStart").length).toBe(preStartSections(false).length);
    expect(subsFor(t, "TestRecord").length).toBeGreaterThan(1);
    // Machine Configuration carries its own tab strip — splitting it again would nest two
    // sub-navigations inside one step.
    expect(subsFor(t, "MachineConfiguration").length).toBe(1);
    expect(subsFor(t, "Setup").length).toBe(1);
    // The pulsation step leads with its ISO 14–15 readings; the faulty-pulsator table comes last.
    const puls = subsFor(t, "PulsatorTest");
    expect(puls.length).toBeGreaterThan(1);
    expect(puls[0].key).not.toBe("rows");
    expect(puls[puls.length - 1].key).toBe("rows");
  });
});
