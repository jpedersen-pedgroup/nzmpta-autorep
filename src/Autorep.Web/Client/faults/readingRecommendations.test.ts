import { describe, it, expect } from "vitest";
import { hasReadingRecommendation, readingRecommendation } from "./readingRecommendations";
import { buildFaultInputs } from "./buildFaults";
import { allReadingSections, type ReadingDef } from "../passfail/standards";
import { defaultMachineConfiguration, type MachineConfiguration } from "../wizard/types";
import type { LocalTest } from "../db/testStore";
import { computeCompleted, stepProgress } from "../wizard/wizardProgress";

// Every optional section switched on, with the readings the cross-reading standards depend on, so
// every reading that can carry a pass/fail rule is in the list.
const everything: MachineConfiguration = {
  ...defaultMachineConfiguration(),
  clusterCount: 20,
  pulsatorCount: 10,
  vsdFitted: true,
  hasAcr: true,
  hasBailGates: true,
  hasMilkMeters: true,
  hasTeatSprayer: true,
  hasBackingGate: true,
  hasReleaserPump: true,
  flushingPulsationSystem: true,
  milklineSize: "75",
};
const inputs = { "tr.workingVacuum": 44, "tr.manualReserve": 800, "tr.pumpCapacityTotal": 2000 };

const def = (key: string, config = everything): ReadingDef =>
  allReadingSections(config, inputs).flatMap((s) => s.readings).find((r) => r.key === key)!;

function makeTest(readings: Record<string, number>, recommendations: Record<string, string> = {}): LocalTest {
  return {
    id: "t1",
    farmName: "Farm",
    config: { ...defaultMachineConfiguration(), clusterCount: 20 },
    currentStep: "FaultSummary",
    visualFaults: {},
    attestations: [],
    readings,
    recommendations,
    dataFields: {},
    createdAt: "2026-09-24T00:00:00.000Z",
    updatedAt: "2026-09-24T00:00:00.000Z",
    markedCompleteAt: null,
    syncState: "local-only",
  };
}

describe("readingRecommendation", () => {
  it("gives every reading that can fail its own wording, not the generic fallback", () => {
    const judged = allReadingSections(everything, inputs)
      .flatMap((s) => s.readings)
      .filter((r) => r.rule.kind !== "none")
      .map((r) => r.key);
    expect(judged.length).toBeGreaterThan(20);
    expect(judged.filter((k) => !hasReadingRecommendation(k))).toEqual([]);
  });

  it("uses the CMM sheet's wording, with the limit the reading was judged against", () => {
    expect(readingRecommendation(def("tr.airlineDropRR"), 2)).toBe(
      "Check for restriction e.g. gland, sanitary trap, undersized pipelines",
    );
    expect(readingRecommendation(def("tr.workingVacuum"), 52)).toMatch(/Reduce vacuum to ≤50 kPa$/);
  });

  it("tells low cluster air admission (12b) apart from high", () => {
    const caa = def("add.clusterAirAdmission"); // 4–12 per cluster × 20 = 80–240
    expect(readingRecommendation(caa, 60)).toMatch(/^Low cluster air admission/);
    expect(readingRecommendation(caa, 300)).toMatch(/^High cluster air admission/);
  });

  it("falls back to a plain instruction for a reading with no wording of its own", () => {
    expect(readingRecommendation({ key: "x.unknown", label: "X", unit: "", rule: { kind: "atMost", limit: 1 } }, 2)).toBe(
      "Investigate and bring within the standard",
    );
  });
});

describe("buildFaultInputs · failed readings", () => {
  it("carries the default recommendation, so the report never prints a blank action", () => {
    const fault = buildFaultInputs(makeTest({ "tr.airlineDropRR": 2 })).find((f) => f.key === "tr.airlineDropRR");
    expect(fault?.recommendation).toBe("Check for restriction e.g. gland, sanitary trap, undersized pipelines");
  });

  it("counts the default as written up, so Fault Summary isn't left looking unfinished", () => {
    const t = makeTest({ "tr.airlineDropRR": 2 });
    expect(stepProgress(t, "FaultSummary")).toBe(1);
    expect(computeCompleted(t).has("FaultSummary")).toBe(true);
  });

  it("keeps what the tester wrote over the default", () => {
    const fault = buildFaultInputs(makeTest({ "tr.airlineDropRR": 2 }, { "tr.airlineDropRR": "Replace the sanitary trap." }))
      .find((f) => f.key === "tr.airlineDropRR");
    expect(fault?.recommendation).toBe("Replace the sanitary trap.");
  });
});
