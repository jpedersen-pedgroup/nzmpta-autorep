// Pins the standards against the NZMPTA Testing Standards Manual + ISO 6690:2007 values
// (10 Jun 2026 audit — see plans/reference/standards-audit.md). The cleaning-reserve and
// effective-reserve cases reproduce the manual's own worked examples.
import { describe, it, expect, afterEach } from "vitest";
import {
  additionalTestSections,
  ancillaryAllowance,
  cleaningReserve,
  pulsatorSections,
  requiredAirflow,
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
  it("pulsator consumption: 30 L/min per 10 units", () => {
    expect(requiredAirflow(10)).toBe(30);
    expect(requiredAirflow(11)).toBe(60);
    expect(requiredAirflow(60)).toBe(180);
  });
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

describe("additional tests rules (manual p41 / ISO C.5)", () => {
  const config = { ...defaultMachineConfiguration(), clusterCount: 20, hasAcr: true };

  it("milk system leakage ≤ 10 + 2 per cluster", () => {
    const secs = additionalTestSections(config);
    expect(reading(secs, "add.milkSystemLeakage").rule).toEqual({ kind: "atMost", limit: 50 });
  });

  it("vacuum system leakage ≤ 5% of pump capacity once capacity is entered", () => {
    expect(reading(additionalTestSections(config), "add.vacuumSystemLeakage").rule).toEqual({ kind: "none" });
    const secs = additionalTestSections(config, { "tr.pumpCapacityTotal": 2000 });
    expect(reading(secs, "add.vacuumSystemLeakage").rule).toEqual({ kind: "atMost", limit: 100 });
  });

  it("pulsator consumption ≤ 30 per 10 units", () => {
    const secs = pulsatorSections(config);
    expect(reading(secs, "puls.pulsatorConsumption").rule).toEqual({ kind: "atMost", limit: 60 });
  });

  it("max chamber vacuum within 2 kPa of working vacuum once 1a is entered", () => {
    const secs = pulsatorSections(config, { "tr.workingVacuum": 48 });
    expect(reading(secs, "puls.maxChamberVacuum").rule).toEqual({ kind: "atLeast", min: 46 });
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
    expect(reading(additionalTestSections(config), "add.milkSystemLeakage").rule).toEqual({ kind: "atMost", limit: 70 });
  });

  it("malformed or absent overrides fall back to the built-in default", () => {
    applyStandardsOverrides([
      { key: "tr.airlineDropRR", label: "", category: "", kind: "atMost", limit: null },
    ]);
    expect(reading(testRecordSections(config), "tr.airlineDropRR").rule).toEqual({ kind: "atMost", limit: 1 });
  });
});

describe("releaser pump minimum speed/power (legacy MinSpeedPowerCal)", () => {
  it("stays capture-only until the head count is entered", () => {
    const config = { ...defaultMachineConfiguration(), clusterCount: 20, hasReleaserPump: true };
    const secs = additionalTestSections(config);
    expect(reading(secs, "add.releaserHeads").rule).toEqual({ kind: "none" });
    expect(reading(secs, "add.releaserSpeed").rule).toEqual({ kind: "none" });
    expect(reading(secs, "add.releaserPower").rule).toEqual({ kind: "none" });
  });

  it("applies the table row for the cluster count × heads (20 × 2 → ≥44 rpm, ≥0.6 kW)", () => {
    const config = { ...defaultMachineConfiguration(), clusterCount: 20, hasReleaserPump: true };
    const secs = additionalTestSections(config, { "add.releaserHeads": 2 });
    expect(reading(secs, "add.releaserSpeed").rule).toEqual({ kind: "atLeast", min: 44 });
    expect(reading(secs, "add.releaserPower").rule).toEqual({ kind: "atLeast", min: 0.6 });
  });

  it("more heads lowers the minimum speed (20 × 4 → ≥22 rpm)", () => {
    const config = { ...defaultMachineConfiguration(), clusterCount: 20, hasReleaserPump: true };
    const secs = additionalTestSections(config, { "add.releaserHeads": 4 });
    expect(reading(secs, "add.releaserSpeed").rule).toEqual({ kind: "atLeast", min: 22 });
  });

  it("has no standard outside the table (44 clusters), matching legacy", () => {
    const config = { ...defaultMachineConfiguration(), clusterCount: 44, hasReleaserPump: true };
    const secs = additionalTestSections(config, { "add.releaserHeads": 2 });
    expect(reading(secs, "add.releaserSpeed").rule).toEqual({ kind: "none" });
    expect(reading(secs, "add.releaserSpeed").hint).toMatch(/no standard/i);
  });
});
