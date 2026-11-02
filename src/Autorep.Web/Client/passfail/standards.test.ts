// Pins the standards against the MPNZ Testing Standards Manual + ISO 6690:2007 values
// (10 Jun 2026 audit — see plans/reference/standards-audit.md). The cleaning-reserve and
// effective-reserve cases reproduce the manual's own worked examples.
import { describe, it, expect, afterEach } from "vitest";
import {
  additionalTestSections,
  airflowSections,
  ancillaryAllowance,
  cleaningReserve,
  pulsatorSections,
  requiredEffectiveReserve,
  testRecordSections,
} from "./standards";
import { applyStandardsOverrides, clearStandardsOverrides } from "./standardsOverrides";
import { defaultMachineConfiguration } from "../wizard/types";

afterEach(() => clearStandardsOverrides());

function reading(sections: ReturnType<typeof testRecordSections>, key: string) {
  for (const s of sections) for (const r of s.readings) if (r.key === key) return r;
  throw new Error(`reading ${key} not found`);
}

describe("requiredEffectiveReserve (manual p42)", () => {
  it("matches the printed table", () => {
    expect(requiredEffectiveReserve(2)).toBe(260);
    expect(requiredEffectiveReserve(10)).toBe(500);
    expect(requiredEffectiveReserve(20)).toBe(600);
    expect(requiredEffectiveReserve(50)).toBe(1350);
    expect(requiredEffectiveReserve(80)).toBe(2100);
  });
  it("keeps growing above 80 clusters (2100 + 25 per cluster) instead of clamping", () => {
    expect(requiredEffectiveReserve(81)).toBe(2125);
    expect(requiredEffectiveReserve(100)).toBe(2600);
    expect(requiredEffectiveReserve(120)).toBe(3100);
  });
});

describe("cleaningReserve (manual p43)", () => {
  it("reproduces the manual's worked examples", () => {
    expect(cleaningReserve("75", 44)).toBe(1125);
    expect(cleaningReserve("50", 46)).toBe(469);
  });
  it("is null without milkline size or vacuum", () => {
    expect(cleaningReserve(null, 44)).toBeNull();
    expect(cleaningReserve("75", null)).toBeNull();
  });
});

describe("allowances (manual p41)", () => {
  it("ACR/meter allowance: 7.5 per unit, min 30, rounded up to 10s, ×2 with bail gates", () => {
    expect(ancillaryAllowance(2, false)).toBe(30);
    expect(ancillaryAllowance(20, false)).toBe(150);
    expect(ancillaryAllowance(21, false)).toBe(160); // 157.5 → 160
    expect(ancillaryAllowance(20, true)).toBe(300);
  });
});

describe("test record rules (manual p40 / ISO 6690 Annex D)", () => {
  const config = { ...defaultMachineConfiguration(), clusterCount: 20 };

  it("receiver→regulator drop ≤ 1, receiver→pump drop ≤ 3", () => {
    const secs = testRecordSections(config);
    expect(reading(secs, "tr.airlineDropRR").rule).toEqual({ kind: "atMost", limit: 1 });
    expect(reading(secs, "tr.airlinePumpDrop").rule).toEqual({ kind: "atMost", limit: 3 });
  });

  it("regulation deviation and gauge errors are signed ±tolerances", () => {
    const secs = testRecordSections(config);
    expect(reading(secs, "tr.regulationDeviation").rule).toEqual({ kind: "tolerance", target: 0, tolerance: 2 });
    expect(reading(secs, "tr.gaugeError1").rule).toEqual({ kind: "tolerance", target: 0, tolerance: 1 });
  });

  it("regulator sensitivity ≤ 1 kPa", () => {
    expect(reading(testRecordSections(config), "tr.regulatorSensitivity").rule).toEqual({ kind: "atMost", limit: 1 });
  });

  it("regulation loss / regulator leakage derive from the manual reserve (10% / 5%, min 35)", () => {
    const secs = testRecordSections(config, { "tr.manualReserve": 800 });
    expect(reading(secs, "tr.regulationLoss").rule).toEqual({ kind: "atMost", limit: 80 });
    expect(reading(secs, "tr.regulatorLeakage").rule).toEqual({ kind: "atMost", limit: 40 });
    // Below the 35 floor the fixed minimum governs.
    const small = testRecordSections(config, { "tr.manualReserve": 200 });
    expect(reading(small, "tr.regulationLoss").rule).toEqual({ kind: "atMost", limit: 35 });
    expect(reading(small, "tr.regulatorLeakage").rule).toEqual({ kind: "atMost", limit: 35 });
  });

  it("effective reserve applies the atmospheric correction to the measured value (÷ factor on the raw threshold)", () => {
    // 20 clusters → 600; at 95 kPa the factor is 1.07 so raw ≥ ceil(600 / 1.07) = 561.
    const secs = testRecordSections({ ...config, atmosPressureSeaLevel: 95 });
    expect(reading(secs, "tr.effectiveReserve").rule).toEqual({ kind: "atLeast", min: 561 });
  });

  it("cleaning reserve governs when a flushing system is fitted (manual p43 worked example)", () => {
    // 20 clusters (ER 600) + 75 mm line @ 44 kPa working vacuum → CR 1125 governs.
    const flushing = {
      ...config,
      flushingPulsationSystem: true,
      milklineSize: "75",
    };
    const secs = testRecordSections(flushing, { "tr.workingVacuum": 44 });
    expect(reading(secs, "tr.effectiveReserve").rule).toEqual({ kind: "atLeast", min: 1125 });
  });
});

describe("airflow tests rules (manual p41 / ISO C.5)", () => {
  const config = { ...defaultMachineConfiguration(), clusterCount: 20, hasAcr: true };

  it("milk system leakage ≤ 10 + 2 per cluster", () => {
    const secs = airflowSections(config);
    expect(reading(secs, "add.milkSystemLeakage").rule).toEqual({ kind: "atMost", limit: 50 });
  });

  it("vacuum system leakage ≤ 5% of the capacity at working vacuum (9b) — the 50 kPa 8a figures don't stand in", () => {
    expect(reading(airflowSections(config), "add.vacuumSystemLeakage").rule).toEqual({ kind: "none" });
    expect(reading(airflowSections(config, { "tr.pumpCapacity1": 3450 }), "add.vacuumSystemLeakage").rule).toEqual({ kind: "none" });
    const secs = airflowSections(config, { "tr.pumpCapacityTotal": 2000 });
    expect(reading(secs, "add.vacuumSystemLeakage").rule).toEqual({ kind: "atMost", limit: 100 });
  });

  it("keeps ISO 10–12 on the airflow step and only the ancillary extras on Additional Tests", () => {
    const airflow = airflowSections(config).map((s) => s.key);
    expect(airflow).toEqual(["AirlineMilkSystemLeakage", "AcrConsumption", "ClusterAirAdmission"]);
    const additional = additionalTestSections({ ...config, hasMilkMeters: true }).map((s) => s.key);
    expect(additional).toEqual(["MilkMeter", "RegulatorLoad", "FinalCheck"]);
  });

  it("folds the pump exhaust (9) into the vacuum-pump section, after the per-pump rows", () => {
    const pump = testRecordSections(config).find((s) => s.key === "VacuumPumpTest")!;
    expect(testRecordSections(config).map((s) => s.key)).not.toContain("PumpExhaustPressure");
    const keys = pump.readings.map((r) => r.key);
    expect(keys.slice(-2)).toEqual(["tr.pumpCapacityTotal", "tr.exhaustPressure"]);
  });

  it("judges the pulsator spreads on the analyser's machine-level extremes", () => {
    const secs = pulsatorSections(config);
    const rate = reading(secs, "puls.rateSpread");
    expect(rate.derived).toBe("fastest − slowest");
    expect(rate.rule).toEqual({ kind: "atMost", limit: 6 });
    const ratio = reading(secs, "puls.ratioSpread");
    expect(ratio.derived).toBe("highest − lowest");
    expect(ratio.rule).toEqual({ kind: "atMost", limit: 5 });
  });

  it("pulsator consumption (14d) is at most 35 per cluster - not per pulsator - with no minimum", () => {
    const def = reading(pulsatorSections(config, { "puls.pulsatorConsumption": 500 }), "puls.pulsatorConsumption");
    expect(def.derived).toBe("14a − 14c");
    expect(def.rule).toEqual({ kind: "atMost", limit: 700 }); // 35 × 20 clusters
    expect(def.hint).toBe("≤ 35 per cluster × 20 = ≤ 700 · 25.0 per cluster");
  });

  it("pulsator consumption (14d) takes an admin per-cluster override, and has no standard without clusters", () => {
    applyStandardsOverrides([{ key: "puls.pulsatorConsumption", label: "", category: "", kind: "atMost", limit: 30 }]);
    expect(reading(pulsatorSections(config), "puls.pulsatorConsumption").rule).toEqual({ kind: "atMost", limit: 600 });
    expect(reading(pulsatorSections({ ...config, clusterCount: 0 }), "puls.pulsatorConsumption").rule).toEqual({ kind: "none" });
  });

  it("test pulsation is judged on the drop (15b = 1a − 15a ≤ 2 kPa), where legacy recorded the verdict", () => {
    const secs = pulsatorSections(config, { "tr.workingVacuum": 48 });
    expect(reading(secs, "puls.maxChamberVacuum").rule).toEqual({ kind: "none" });
    const drop = reading(secs, "puls.testPulsationReading");
    expect(drop.derived).toBe("1a − 15a");
    expect(drop.rule).toEqual({ kind: "atMost", limit: 2 });
  });
});

describe("vacuum pump OEM capacity (8a)", () => {
  const config = { ...defaultMachineConfiguration(), clusterCount: 20 };

  it("works the spec out beside 8a once the pump has an OEM spec", () => {
    const secs = testRecordSections({ ...config, vacuumPumps: [{ make: "De Laval", model: "DVP1600" }] });
    const pump = secs.find((s) => s.key === "VacuumPumpTest")!;
    expect(pump.readings.map((r) => r.key).slice(0, 2)).toEqual(["tr.pumpCapacity1", "tr.pumpOemCapacity1"]);
    expect(pump.readings.map((r) => r.key).slice(-2)).toEqual(["tr.pumpCapacityTotal", "tr.exhaustPressure"]);
    const oem = reading(secs, "tr.pumpOemCapacity1");
    expect(oem.derived).toBe("8c x the OEM spec's L/min per rpm");
    expect(oem.rule).toEqual({ kind: "none" });
    expect(oem.hint).toMatch(/1\.15 L\/min per rpm/);
    expect(oem.hint).toMatch(/no verdict/);
  });

  it("says nothing about the OEM figure for a pump typed in by hand", () => {
    const secs = testRecordSections({ ...config, vacuumPumps: [{ make: "Acme", model: "Hyperpump" }] });
    const keys = secs.find((s) => s.key === "VacuumPumpTest")!.readings.map((r) => r.key);
    expect(keys).not.toContain("tr.pumpOemCapacity1");
    expect(reading(secs, "tr.pumpCapacity1").hint).toMatch(/compare to OEM curve/);
  });

  it("warns when the speed 8a was measured at is outside the OEM spec's range", () => {
    const secs = testRecordSections(
      { ...config, vacuumPumps: [{ make: "De Laval", model: "DVP1600" }] },
      { "tr.pumpMaxSpeed1": 1600 }, // the OEM spec tops out at 1400
    );
    expect(reading(secs, "tr.pumpOemCapacity1").hint).toMatch(/outside that range/);
  });

  it("keys the rows per pump, so pump 2's spec comes from pump 2's model", () => {
    const secs = testRecordSections({
      ...config,
      numberOfVacuumPumps: 2,
      vacuumPumps: [{ make: "Acme", model: "Hyperpump" }, { make: "De Laval", model: "DVP2300" }],
    });
    const keys = secs.find((s) => s.key === "VacuumPumpTest")!.readings.map((r) => r.key);
    expect(keys).not.toContain("tr.pumpOemCapacity1");
    expect(keys).toContain("tr.pumpOemCapacity2");
    expect(reading(secs, "tr.pumpOemCapacity2").hint).toMatch(/DVP2300/);
  });
});

describe("admin-managed standard overrides", () => {
  const config = { ...defaultMachineConfiguration(), clusterCount: 20 };

  it("a synced rule row replaces the built-in default", () => {
    applyStandardsOverrides([
      { key: "tr.airlineDropRR", label: "", category: "", kind: "atMost", limit: 1.5 },
    ]);
    expect(reading(testRecordSections(config), "tr.airlineDropRR").rule).toEqual({ kind: "atMost", limit: 1.5 });
  });

  it("a synced param row feeds the formula", () => {
    applyStandardsOverrides([
      { key: "param.milkLeak.perCluster", label: "", category: "", kind: "param", value: 3 },
    ]);
    // 10 + 3 × 20 clusters = 70 (default 2/cluster would give 50).
    expect(reading(airflowSections(config), "add.milkSystemLeakage").rule).toEqual({ kind: "atMost", limit: 70 });
  });

  it("malformed or absent overrides fall back to the built-in default", () => {
    applyStandardsOverrides([
      { key: "tr.airlineDropRR", label: "", category: "", kind: "atMost", limit: null },
    ]);
    expect(reading(testRecordSections(config), "tr.airlineDropRR").rule).toEqual({ kind: "atMost", limit: 1 });
  });
});

describe("releaser pump minimum speed/power (legacy MinSpeedPowerCal)", () => {
  const diaphragm = (clusterCount: number) => ({
    ...defaultMachineConfiguration(),
    clusterCount,
    hasReleaserPump: true,
    releaserPumps: [{ make: "EFFCO / FLYNN", model: "DIAPHRAGM" }],
  });

  it("stays capture-only until the head count is entered", () => {
    const secs = additionalTestSections(diaphragm(20));
    expect(reading(secs, "add.releaserHeads").rule).toEqual({ kind: "none" });
    expect(reading(secs, "add.releaserSpeed").rule).toEqual({ kind: "none" });
    expect(reading(secs, "add.releaserPower").rule).toEqual({ kind: "none" });
  });

  it("applies the table row for the cluster count × heads (20 × 2 → ≥44 spm, ≥0.6 kW)", () => {
    const secs = additionalTestSections(diaphragm(20), { "add.releaserHeads": 2 });
    expect(reading(secs, "add.releaserSpeed").rule).toEqual({ kind: "atLeast", min: 44 });
    expect(reading(secs, "add.releaserPower").rule).toEqual({ kind: "atLeast", min: 0.6 });
  });

  it("more heads lowers the minimum speed (20 × 4 → ≥22 spm)", () => {
    const secs = additionalTestSections(diaphragm(20), { "add.releaserHeads": 4 });
    expect(reading(secs, "add.releaserSpeed").rule).toEqual({ kind: "atLeast", min: 22 });
  });

  it("reads a diaphragm pump's speed in strokes per minute", () => {
    const speed = reading(additionalTestSections(diaphragm(12), { "add.releaserHeads": 2 }), "add.releaserSpeed");
    expect(speed.unit).toBe("spm");
    expect(speed.hint).toContain("≥ 26 spm");
  });

  it("has no standard outside the table (44 clusters), matching legacy", () => {
    const secs = additionalTestSections(diaphragm(44), { "add.releaserHeads": 2 });
    expect(reading(secs, "add.releaserSpeed").rule).toEqual({ kind: "none" });
    expect(reading(secs, "add.releaserSpeed").hint).toMatch(/no standard/i);
  });

  // Jono, 2 Nov 2026: 12 clusters and no verdict. The table has no 1-head row at that size, so the
  // tester has to be told what it does have.
  it("names the head counts the table has when the one entered isn't there", () => {
    const secs = additionalTestSections(diaphragm(12), { "add.releaserHeads": 1 });
    expect(reading(secs, "add.releaserSpeed").rule).toEqual({ kind: "none" });
    expect(reading(secs, "add.releaserSpeed").hint).toBe("no standard for 1 head · the table has 2 or 3 heads for 12 clusters");
    expect(reading(secs, "add.releaserHeads").hint).toContain("2 or 3 heads for 12 clusters");
  });

  it("judges only a diaphragm pump: any other releaser has no heads, reads in rpm, and has no verdict", () => {
    const config = { ...diaphragm(20), releaserPumps: [{ make: "DELAVAL", model: "FMP220 CENTRIFUGAL" }] };
    const secs = additionalTestSections(config, { "add.releaserHeads": 2 });
    expect(secs.find((s) => s.key === "ReleaserPumpHeads")!.readings.map((r) => r.key)).toEqual(["add.releaserSpeed", "add.releaserPower"]);
    expect(reading(secs, "add.releaserSpeed")).toMatchObject({ unit: "rpm", rule: { kind: "none" } });
    expect(reading(secs, "add.releaserSpeed").hint).toBeUndefined();
    expect(reading(secs, "add.releaserPower").rule).toEqual({ kind: "none" });
  });

  it("points at Machine Configuration when no pump has been chosen", () => {
    const config = { ...diaphragm(20), releaserPumps: [] };
    expect(reading(additionalTestSections(config), "add.releaserSpeed").hint).toMatch(/Machine Configuration/);
  });
});

describe("peak regulator load (manual p61)", () => {
  const config = { ...defaultMachineConfiguration(), clusterCount: 20 };

  it("takes the actual reading and calculates the increase on 1a, judged at ≤ 2 kPa", () => {
    const secs = additionalTestSections(config, { "tr.workingVacuum": 46 });
    expect(reading(secs, "add.peakRegulatorReading")).toMatchObject({ unit: "kPa", rule: { kind: "none" } });
    expect(reading(secs, "add.peakRegulatorReading").derived).toBeUndefined();
    const load = reading(secs, "add.regulatorLoad");
    expect(load.derived).toBe("reading − 1a");
    expect(load.rule).toEqual({ kind: "atMost", limit: 2 });
    expect(load.hint).toBe("≤ 2 kPa above working vacuum (46)");
  });

  it("asks for 1a first", () => {
    expect(reading(additionalTestSections(config), "add.regulatorLoad").hint).toContain("enter 1a first");
  });

  it("is still the admin-managed add.regulatorLoad row", () => {
    applyStandardsOverrides([{ key: "add.regulatorLoad", kind: "atMost", limit: 3, version: 1 }] as never);
    expect(reading(additionalTestSections(config), "add.regulatorLoad").rule).toEqual({ kind: "atMost", limit: 3 });
  });
});

describe("final check (16a/16b)", () => {
  const config = { ...defaultMachineConfiguration(), clusterCount: 20 };

  it("is the last section of Additional Tests, on every machine", () => {
    expect(additionalTestSections(config).at(-1)?.key).toBe("FinalCheck");
  });

  it("records the two figures and their change, with no verdict until MPNZ sets limits", () => {
    const secs = additionalTestSections(config, { "tr.workingVacuum": 46, "tr.effectiveReserve": 700 });
    const final = secs.find((s) => s.key === "FinalCheck")!;
    expect(final.readings.map((r) => r.key)).toEqual([
      "add.finalWorkingVacuum", "add.finalVacuumChange", "add.finalEffectiveReserve", "add.finalReserveChange",
    ]);
    expect(final.readings.every((r) => r.rule.kind === "none")).toBe(true);
    expect(reading(secs, "add.finalWorkingVacuum").hint).toBe("46 at 1a");
    expect(reading(secs, "add.finalEffectiveReserve").hint).toBe("700 at 2a");
  });
});

describe("pulsator readings Jono asked to be worded", () => {
  const secs = pulsatorSections({ ...defaultMachineConfiguration(), clusterCount: 20 });

  it("calls 14a and 14e ancillary equipment", () => {
    expect(reading(secs, "puls.airflowMilkSystem").label).toBe("Airflow — milk system ancillary equipment (14a)");
    expect(reading(secs, "puls.airflowVacuumSystem").label).toBe("Airflow — vacuum system ancillary equipment (14e)");
  });

  it("shows the airline stability limit beside the reading", () => {
    expect(reading(secs, "puls.airlineStability").hint).toBe("≤ 4 kPa");
  });
});
