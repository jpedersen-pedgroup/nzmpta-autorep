import { describe, it, expect } from "vitest";
import { DERIVED_READINGS, deriveReadings, formulaFor, isDerivedReading } from "./derived";
import { allReadingSections } from "./standards";
import { evaluate } from "./passFail";
import { defaultMachineConfiguration, type MachineConfiguration } from "../wizard/types";

const cfg = (over: Partial<MachineConfiguration> = {}): MachineConfiguration => ({
  ...defaultMachineConfiguration(),
  clusterCount: 20,
  ...over,
});

function verdictOf(config: MachineConfiguration, readings: Record<string, number>, key: string) {
  for (const s of allReadingSections(config, readings)) {
    for (const r of s.readings) if (r.key === key) return evaluate(readings[key], r.rule);
  }
  throw new Error(`reading ${key} not found`);
}

describe("deriveReadings", () => {
  it("fills every legacy-calculated reading from its inputs", () => {
    // ACRs fitted so the 11b row exists — formulas only run for rows this machine shows.
    const r = deriveReadings(cfg({ hasAcr: true }), {
      "tr.workingVacuum": 43, "tr.nominalVacuum": 45,
      "tr.effectiveReserve": 3440, "tr.reserveAirflow": 3440, "tr.manualReserve": 3500, "tr.regulatorLeakageAirflow": 3460,
      "tr.avgReceiverVacuum": 41, "tr.minVacuumAirInlet": 40.5, "tr.avgVacuumAirInlet": 40.9, "tr.maxVacuumIncrease": 41.4, "tr.avgVacuumStopAirInlet": 41,
      "tr.airlineVacReceiver": 39, "tr.airlineVacRegulator": 39.4, "tr.airlineVacPump": 40,
      "tr.regSensWorkingVac": 43.5,
      "tr.farmGauge1": 36, "tr.testGauge1": 36.5, "tr.farmGauge2": 41, "tr.testGauge2": 41, "tr.farmGauge3": 46, "tr.testGauge3": 45,
      "tr.pumpCapacityTotal": 4480, "add.airflowVacuumSystem": 4400, "add.airflowMilkSystem": 4320, "add.acrAirflow": 4300,
      "add.clusterAirAdmissionConnect": 3850,
      "puls.airflowMilkSystem": 3840, "puls.airflowPulsators": 3060, "puls.airflowVacuumSystem": 3050,
      "puls.maxChamberVacuum": 40,
    });
    expect(r["tr.regulationDeviation"]).toBe(-2);
    expect(r["tr.regulationLoss"]).toBe(60);
    expect(r["tr.regulatorLeakage"]).toBe(20);
    expect(r["tr.requiredEffectiveReserve"]).toBe(600); // 20 clusters, manual p42
    expect(r["tr.requiredCleaningReserve"]).toBeUndefined(); // no flushing system
    expect(r["tr.fallOff"]).toBe(0.1);
    expect(r["tr.regulationUndershoot"]).toBe(0.4);
    expect(r["tr.regulationOvershoot"]).toBe(0.4);
    expect(r["tr.airlineDropRR"]).toBe(0.4);
    expect(r["tr.airlinePumpDrop"]).toBe(1);
    expect(r["tr.regulatorSensitivity"]).toBe(0.5);
    expect(r["tr.gaugeError1"]).toBe(-0.5);
    expect(r["tr.gaugeError2"]).toBe(0);
    expect(r["tr.gaugeError3"]).toBe(1);
    expect(r["add.vacuumSystemLeakage"]).toBe(80);
    expect(r["add.milkSystemLeakage"]).toBe(80);
    expect(r["add.acrConsumption"]).toBe(20);
    expect(r["add.clusterAirAdmission"]).toBe(470);
    expect(r["puls.milkSystemAncillary"]).toBe(10);
    expect(r["puls.pulsatorConsumption"]).toBe(780);
    expect(r["puls.vacuumSystemAncillary"]).toBe(10);
    expect(r["puls.testPulsationReading"]).toBe(3);
  });

  it("leaves a derived reading blank while an input is missing, and removes a stale one", () => {
    // Legacy showed −45 ✗ with 1a blank; a blank is the honest answer.
    expect(deriveReadings(cfg(), { "tr.nominalVacuum": 45 })["tr.regulationDeviation"]).toBeUndefined();
    // A value typed under a derived key by an older build goes once the formula can't stand it up.
    expect(deriveReadings(cfg(), { "tr.regulationDeviation": -1 })["tr.regulationDeviation"]).toBeUndefined();
    // …and is replaced, not kept, once the inputs exist.
    const r = deriveReadings(cfg(), { "tr.regulationDeviation": -1, "tr.workingVacuum": 41, "tr.nominalVacuum": 45 });
    expect(r["tr.regulationDeviation"]).toBe(-4);
  });

  it("judges the pulsator spreads on the analyser's extremes, not on the faulty rows", () => {
    const r = deriveReadings(cfg(), { "puls.rateFastest": 60, "puls.rateSlowest": 52, "puls.ratioHighest": 61.2, "puls.ratioLowest": 58 });
    expect(r["puls.rateSpread"]).toBe(8);
    expect(r["puls.ratioSpread"]).toBe(3.2);
    expect(verdictOf(cfg(), r, "puls.rateSpread")).toBe("fail"); // 8 > 6 ppm
    expect(verdictOf(cfg(), r, "puls.ratioSpread")).toBe("pass"); // 3.2 ≤ 5%
  });

  it("still fails a spread whose extremes were keyed the wrong way round", () => {
    // 52 typed as the fastest and 60 as the slowest: a signed 52 − 60 = −8 would PASS "≤ 6".
    const r = deriveReadings(cfg(), { "puls.rateFastest": 52, "puls.rateSlowest": 60, "puls.ratioHighest": 58, "puls.ratioLowest": 64.5 });
    expect(r["puls.rateSpread"]).toBe(8);
    expect(r["puls.ratioSpread"]).toBe(6.5);
    expect(verdictOf(cfg(), r, "puls.rateSpread")).toBe("fail");
    expect(verdictOf(cfg(), r, "puls.ratioSpread")).toBe("fail");
  });

  it("does not touch measured readings", () => {
    const r = deriveReadings(cfg(), { "tr.workingVacuum": 41.5, "tr.nominalVacuum": 45 });
    expect(r["tr.workingVacuum"]).toBe(41.5);
    expect(r["tr.nominalVacuum"]).toBe(45);
  });

  it("rounds to one decimal, half away from zero on both sides, and never yields −0", () => {
    expect(deriveReadings(cfg(), { "tr.avgReceiverVacuum": 0.3, "tr.avgVacuumAirInlet": 0.1 })["tr.fallOff"]).toBe(0.2);
    // Math.round on its own would take −3.45 to −3.4 but 3.45 to 3.5.
    expect(deriveReadings(cfg(), { "tr.workingVacuum": 41.55, "tr.nominalVacuum": 45 })["tr.regulationDeviation"]).toBe(-3.5);
    expect(deriveReadings(cfg(), { "tr.workingVacuum": 48.45, "tr.nominalVacuum": 45 })["tr.regulationDeviation"]).toBe(3.5);
    const zero = deriveReadings(cfg(), { "tr.farmGauge1": 41.02, "tr.testGauge1": 41.04 })["tr.gaugeError1"];
    expect(Object.is(zero, -0)).toBe(false);
    expect(zero).toBe(0);
  });

  it("only derives rows this machine shows, so a hidden key can't linger after a config change", () => {
    const withAcr = cfg({ hasAcr: true });
    const readings = { "add.airflowMilkSystem": 4320, "add.acrAirflow": 4300 };
    expect(deriveReadings(withAcr, readings)["add.acrConsumption"]).toBe(20);
    expect(deriveReadings(cfg({ hasAcr: false }), { ...readings, "add.acrConsumption": 20 })["add.acrConsumption"]).toBeUndefined();
  });

  it("is a single pass: no formula feeds another, so deriving twice changes nothing", () => {
    const derivedKeys = new Set(DERIVED_READINGS.map((d) => d.key));
    for (const d of DERIVED_READINGS) {
      for (const input of d.inputs) expect(derivedKeys.has(input), `${d.key} ← ${input}`).toBe(false);
    }
    const once = deriveReadings(cfg(), { "tr.workingVacuum": 41.5, "tr.nominalVacuum": 45, "tr.regSensWorkingVac": 42 });
    expect(deriveReadings(cfg(), once)).toEqual(once);
  });

  it("shows the required cleaning reserve only when a flushing system is fitted", () => {
    // Manual p43 worked example: 75 mm line at 44 kPa → 1125.
    const flushing = cfg({ flushingPulsationSystem: true, milklineSize: "75" });
    expect(deriveReadings(flushing, { "tr.workingVacuum": 44 })["tr.requiredCleaningReserve"]).toBe(1125);
    expect(deriveReadings(flushing, {})["tr.requiredCleaningReserve"]).toBeUndefined();
    expect(deriveReadings(cfg({ milklineSize: "75" }), { "tr.workingVacuum": 44 })["tr.requiredCleaningReserve"]).toBeUndefined();
  });

  it("removes a config-only derived value when the config no longer supports it", () => {
    expect(deriveReadings(cfg({ clusterCount: 0 }), { "tr.requiredEffectiveReserve": 600 })["tr.requiredEffectiveReserve"]).toBeUndefined();
  });
});

describe("derived readings and the reading definitions agree", () => {
  it("every derived key is defined as calculated in the sections, with the same formula, and nothing else is", () => {
    // A config with everything fitted so every conditional section is present.
    const everything = cfg({ vsdFitted: true, hasAcr: true, hasMilkMeters: true, hasTeatSprayer: true, hasBailGates: true, hasReleaserPump: true, flushingPulsationSystem: true, numberOfVacuumPumps: 2 });
    const defs = new Map(allReadingSections(everything, {}).flatMap((s) => s.readings).map((r) => [r.key, r]));
    for (const d of DERIVED_READINGS) {
      expect(defs.get(d.key)?.derived, d.key).toBe(d.formula);
    }
    for (const [key, def] of defs) {
      if (def.derived) expect(isDerivedReading(key), key).toBe(true);
    }
    expect(() => formulaFor("tr.workingVacuum")).toThrow();
  });
});

describe("replay of the 11 Sep 2026 test (legacy screens, 37-cluster herringbone)", () => {
  // Values read off the legacy Test Record during the 15 Sep review. Every calculated figure must
  // match what legacy showed the tester, and the verdicts must match its ticks and crosses.
  const config = cfg({ clusterCount: 37, pulsatorCount: 19 });
  const measured: Record<string, number> = {
    "tr.workingVacuum": 41.5, "tr.nominalVacuum": 45,
    "tr.effectiveReserve": 3440, "tr.reserveAirflow": 3440, "tr.manualReserve": 3440, "tr.regulatorLeakageAirflow": 3440,
    "tr.regSensWorkingVac": 41.5,
    "tr.pumpCapacityTotal": 4480, "add.airflowVacuumSystem": 4400, "add.airflowMilkSystem": 4320,
    "add.clusterAirAdmissionConnect": 3850,
    "puls.airflowMilkSystem": 3850, "puls.airflowPulsators": 3060, "puls.airflowVacuumSystem": 3050,
    "puls.maxChamberVacuum": 40,
  };
  const r = deriveReadings(config, measured);

  it("calculates what legacy calculated", () => {
    expect(r["tr.regulationDeviation"]).toBe(-3.5);
    expect(r["tr.regulationLoss"]).toBe(0);
    expect(r["tr.regulatorLeakage"]).toBe(0);
    expect(r["tr.regulatorSensitivity"]).toBe(0);
    expect(r["add.vacuumSystemLeakage"]).toBe(80);
    expect(r["add.milkSystemLeakage"]).toBe(80);
    expect(r["add.clusterAirAdmission"]).toBe(470);
    expect(r["puls.milkSystemAncillary"]).toBe(0);
    expect(r["puls.pulsatorConsumption"]).toBe(790);
    expect(r["puls.vacuumSystemAncillary"]).toBe(10);
    expect(r["puls.testPulsationReading"]).toBe(1.5);
    expect(r["tr.requiredEffectiveReserve"]).toBe(1050); // legacy 2g
  });

  it("judges them as legacy did", () => {
    expect(verdictOf(config, r, "tr.regulationDeviation")).toBe("fail"); // −3.5 outside ±2 ✗ (the typed −1 had passed)
    expect(verdictOf(config, r, "tr.regulationLoss")).toBe("pass");
    expect(verdictOf(config, r, "tr.regulatorLeakage")).toBe("pass");
    expect(verdictOf(config, r, "tr.regulatorSensitivity")).toBe("pass");
    expect(verdictOf(config, r, "add.vacuumSystemLeakage")).toBe("pass"); // 80 ≤ 5% of 4480
    expect(verdictOf(config, r, "add.milkSystemLeakage")).toBe("pass"); // 80 ≤ 10 + 2 × 37
    expect(verdictOf(config, r, "add.clusterAirAdmission")).toBe("fail"); // 470 outside 148–444 for 37 clusters
    expect(verdictOf(config, r, "puls.testPulsationReading")).toBe("pass"); // 1.5 ≤ 2
    // 14d passed in legacy at 790; the app's limit is unconfirmed, so no verdict either way.
    expect(verdictOf(config, r, "puls.pulsatorConsumption")).toBe("noStandard");
  });
});

describe("12b is judged as a machine total", () => {
  const rule = (config: MachineConfiguration) =>
    allReadingSections(config, {}).flatMap((s) => s.readings).find((d) => d.key === "add.clusterAirAdmission")!.rule;

  it("scales the 4–12 per-cluster band by the cluster count", () => {
    expect(rule(cfg({ clusterCount: 10 }))).toEqual({ kind: "between", min: 40, max: 120 });
    expect(rule(cfg({ clusterCount: 37 }))).toEqual({ kind: "between", min: 148, max: 444 });
  });

  it("uses the vented-liner ceiling × clusters, and tells the tester that is unconfirmed", () => {
    const vented = cfg({ clusterCount: 10, linerVented: true });
    expect(rule(vented)).toEqual({ kind: "atMost", limit: 350 });
    const def = allReadingSections(vented, {}).flatMap((s) => s.readings).find((d) => d.key === "add.clusterAirAdmission")!;
    expect(def.hint).toMatch(/unconfirmed with NZMPTA/);
  });

  it("has no standard until the cluster count is set", () => {
    expect(rule(cfg({ clusterCount: 0 }))).toEqual({ kind: "none" });
  });
});
