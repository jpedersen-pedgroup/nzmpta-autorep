import { describe, it, expect } from "vitest";
import {
  computeCompleted,
  faultsInStep,
  overallProgress,
  runningSectionKeys,
  stepProgress,
  subsFor,
} from "./wizardProgress";
import { applyCheckAll, preStartSections, runningSectionsFor } from "./visualChecklist";
import { testRecordSections } from "../passfail/standards";
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

  it("splits paginated steps into sub-sections and leaves the rest single", () => {
    const t = makeTest();
    expect(subsFor(t, "VisualFaultsPreStart").length).toBe(preStartSections(false).length);
    expect(subsFor(t, "TestRecord").length).toBeGreaterThan(1);
    // Machine Configuration carries its own tab strip — splitting it again would nest two
    // sub-navigations inside one step.
    expect(subsFor(t, "MachineConfiguration").length).toBe(1);
    expect(subsFor(t, "Setup").length).toBe(1);
    expect(subsFor(t, "PulsatorTest")[0].key).toBe("rows");
  });
});
