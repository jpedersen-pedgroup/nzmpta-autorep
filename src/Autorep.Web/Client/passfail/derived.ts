// Readings the legacy AutoRep worked out for the tester — every "(x − y)" value on its Test Record —
// which the rebuild had left as empty boxes to type into. That produced a wrong verdict on a real
// test (1c typed as −1 → PASS, when 1a − 1b was −4). Each entry names the reading it fills, the
// readings it needs and the formula the tester sees; deriveReadings() is the one place these values
// are written, so progress, the fault list, the report and the amendment history all see the same
// numbers.
//
// A derived key is OWNED by its formula: written whenever every input is present, removed when one
// is missing (a stale figure is worse than a blank). Values are rounded to 0.1 — legacy printed one
// decimal (1c −3.5) — and the rounded figure is what the pass/fail rule judges and the report prints.
// Migrated and completed tests are never edited, so their as-recorded values are untouched.
import type { MachineConfiguration } from "../wizard/types";
import { allReadingSections, cleaningReserve, requiredEffectiveReserve } from "./standards";

export interface DerivedReading {
  key: string;
  /** Reading keys the formula needs — all must be present before a value is written. */
  inputs: string[];
  /** Shown to the tester next to the value, in the flowchart's own terms. */
  formula: string;
  calc: (readings: Record<string, number>, config: MachineConfiguration) => number | null;
}

const minus = (a: string, b: string) => (r: Record<string, number>) => r[a] - r[b];

export const DERIVED_READINGS: DerivedReading[] = [
  // 1 — System vacuum. Signed: negative when the receiver sits below nominal.
  { key: "tr.regulationDeviation", inputs: ["tr.workingVacuum", "tr.nominalVacuum"], formula: "1a − 1b", calc: minus("tr.workingVacuum", "tr.nominalVacuum") },
  // 2 — Reserve. 2g/2h are the standards the tester compares 2a against; legacy showed them as fields.
  { key: "tr.regulationLoss", inputs: ["tr.manualReserve", "tr.effectiveReserve"], formula: "2c − 2a", calc: minus("tr.manualReserve", "tr.effectiveReserve") },
  { key: "tr.regulatorLeakage", inputs: ["tr.regulatorLeakageAirflow", "tr.reserveAirflow"], formula: "2e − 2b", calc: minus("tr.regulatorLeakageAirflow", "tr.reserveAirflow") },
  { key: "tr.requiredEffectiveReserve", inputs: [], formula: "manual p42 table, by cluster count", calc: (_r, c) => requiredEffectiveReserve(c.clusterCount) },
  {
    key: "tr.requiredCleaningReserve",
    inputs: ["tr.workingVacuum"],
    formula: "milkline size & 1a (manual p43)",
    calc: (r, c) => (c.flushingPulsationSystem ? cleaningReserve(c.milklineSize, r["tr.workingVacuum"]) : null),
  },
  // 3 — Regulation
  { key: "tr.fallOff", inputs: ["tr.avgReceiverVacuum", "tr.avgVacuumAirInlet"], formula: "3a − 3c", calc: minus("tr.avgReceiverVacuum", "tr.avgVacuumAirInlet") },
  { key: "tr.regulationUndershoot", inputs: ["tr.avgVacuumAirInlet", "tr.minVacuumAirInlet"], formula: "3c − 3b", calc: minus("tr.avgVacuumAirInlet", "tr.minVacuumAirInlet") },
  { key: "tr.regulationOvershoot", inputs: ["tr.maxVacuumIncrease", "tr.avgVacuumStopAirInlet"], formula: "3d − 3e", calc: minus("tr.maxVacuumIncrease", "tr.avgVacuumStopAirInlet") },
  // 4 — Airline drop
  { key: "tr.airlineDropRR", inputs: ["tr.airlineVacRegulator", "tr.airlineVacReceiver"], formula: "4b − 4a", calc: minus("tr.airlineVacRegulator", "tr.airlineVacReceiver") },
  { key: "tr.airlinePumpDrop", inputs: ["tr.airlineVacPump", "tr.airlineVacReceiver"], formula: "4d − 4a", calc: minus("tr.airlineVacPump", "tr.airlineVacReceiver") },
  // 5 — Regulator sensitivity
  { key: "tr.regulatorSensitivity", inputs: ["tr.regSensWorkingVac", "tr.workingVacuum"], formula: "5a − 1a", calc: minus("tr.regSensWorkingVac", "tr.workingVacuum") },
  // 7 — Gauge accuracy: farm gauge minus test gauge at each point.
  { key: "tr.gaugeError1", inputs: ["tr.farmGauge1", "tr.testGauge1"], formula: "7a − 7b", calc: minus("tr.farmGauge1", "tr.testGauge1") },
  { key: "tr.gaugeError2", inputs: ["tr.farmGauge2", "tr.testGauge2"], formula: "7d − 7e", calc: minus("tr.farmGauge2", "tr.testGauge2") },
  { key: "tr.gaugeError3", inputs: ["tr.farmGauge3", "tr.testGauge3"], formula: "7g − 7h", calc: minus("tr.farmGauge3", "tr.testGauge3") },
  // 10 — Leakage. 9b is legacy's "Air Flow Start: pump capacity at working vacuum" — the baseline.
  { key: "add.vacuumSystemLeakage", inputs: ["tr.pumpCapacityTotal", "add.airflowVacuumSystem"], formula: "9b − 10a", calc: minus("tr.pumpCapacityTotal", "add.airflowVacuumSystem") },
  { key: "add.milkSystemLeakage", inputs: ["add.airflowVacuumSystem", "add.airflowMilkSystem"], formula: "10a − 10c", calc: minus("add.airflowVacuumSystem", "add.airflowMilkSystem") },
  // 11 — ACR
  { key: "add.acrConsumption", inputs: ["add.airflowMilkSystem", "add.acrAirflow"], formula: "10c − 11a", calc: minus("add.airflowMilkSystem", "add.acrAirflow") },
  // 12 — Cluster air admission, the TOTAL for the machine (judged per cluster × cluster count).
  { key: "add.clusterAirAdmission", inputs: ["add.airflowMilkSystem", "add.clusterAirAdmissionConnect"], formula: "10c − 12a", calc: minus("add.airflowMilkSystem", "add.clusterAirAdmissionConnect") },
  // 14 — Pulsator & ancillary consumption
  { key: "puls.milkSystemAncillary", inputs: ["add.clusterAirAdmissionConnect", "puls.airflowMilkSystem"], formula: "12a − 14a", calc: minus("add.clusterAirAdmissionConnect", "puls.airflowMilkSystem") },
  { key: "puls.pulsatorConsumption", inputs: ["puls.airflowMilkSystem", "puls.airflowPulsators"], formula: "14a − 14c", calc: minus("puls.airflowMilkSystem", "puls.airflowPulsators") },
  { key: "puls.vacuumSystemAncillary", inputs: ["puls.airflowPulsators", "puls.airflowVacuumSystem"], formula: "14c − 14e", calc: minus("puls.airflowPulsators", "puls.airflowVacuumSystem") },
  // 15 — Test pulsation: the pulsator airline drop below the working vacuum.
  { key: "puls.testPulsationReading", inputs: ["tr.workingVacuum", "puls.maxChamberVacuum"], formula: "1a − 15a", calc: minus("tr.workingVacuum", "puls.maxChamberVacuum") },
];

const BY_KEY = new Map(DERIVED_READINGS.map((d) => [d.key, d]));

export function isDerivedReading(key: string): boolean {
  return BY_KEY.has(key);
}

/** The formula text for a derived key; throws for anything else so a reading definition can't
 * claim to be calculated without a formula behind it. */
export function formulaFor(key: string): string {
  const d = BY_KEY.get(key);
  if (!d) throw new Error(`No derivation defined for reading ${key}`);
  return d.formula;
}

/** Round to 0.1, half away from zero either side (Math.round alone takes −3.45 to −3.4 but 3.45 to
 * 3.5), and never −0. */
function round1(v: number): number {
  const r = (Math.sign(v) * Math.round(Math.abs(v) * 10)) / 10;
  return r === 0 ? 0 : r;
}

/** The readings with every derived value (re)computed from its inputs. Pure — returns a new map.
 * A formula whose row this machine doesn't show (11b without ACRs, 2h without a flushing system)
 * is cleared rather than computed, so no hidden key lingers after a configuration change. */
export function deriveReadings(
  config: MachineConfiguration,
  readings: Record<string, number>,
): Record<string, number> {
  const out = { ...readings };
  const shown = new Set(allReadingSections(config, out).flatMap((s) => s.readings.map((r) => r.key)));
  for (const d of DERIVED_READINGS) {
    const ready = shown.has(d.key) && d.inputs.every((k) => out[k] != null && !Number.isNaN(out[k]));
    const v = ready ? d.calc(out, config) : null;
    if (v == null || !Number.isFinite(v)) delete out[d.key];
    else out[d.key] = round1(v);
  }
  return out;
}
