// Adapts a MIGRATED legacy test's PayloadJson (raw legacy column shape: record1/2/3, additional…)
// into the new wizard's reading keys + the original "as-recorded" pass/fail verdict per reading.
//
// Source shape (from the migration tool): each legacy reading column comes as a *E value (a STRING,
// e.g. "45", "-0.2", "") and an optional *O rating code (a NUMBER). Verdicts are taken as-recorded
// from the *O code — never recomputed (legacy thresholds differ subtly: atmospheric correction,
// cleaning-reserve governing, tester overrides). See the rating-code decode below.
//
// This module covers the numerical readings (ISO groups 1–15), recommendations, recorded visual
// faults, and individual-cluster rows. Per-pulsator rows are deferred (legacy channel + phase-unit
// semantics need confirmation before they can be mapped without mislabeling).
import type { MeasurementRow } from "../db/testStore";

export type LegacyVerdict = "pass" | "fail";

/**
 * Legacy reading O-code → as-recorded verdict. Verified against the live DB (100%-clean threshold
 * cross-tabs): 2 = entered & meets standard (pass), 3 = entered & breaches (fail), 1 = no reading
 * recorded (blank E), 0 = not applicable. Legacy has NO "warning" tier.
 */
export function decodeReadingVerdict(code: number | null | undefined): LegacyVerdict | null {
  if (code === 2) return "pass";
  if (code === 3) return "fail";
  return null; // 1 (no reading) / 0 (N/A) — nothing to show
}

/**
 * Legacy visual/pulsation status code (CheckStatus / TicCross, domain {1,2,3}) → verdict.
 * 2 = pass, 3 = fail, 1 = blank/no-fault. On visual-fault tables only code 3 carries a real fault.
 */
export function decodeStatusVerdict(code: number | null | undefined): LegacyVerdict | null {
  if (code === 3) return "fail";
  if (code === 2) return "pass";
  return null; // 1 = blank / not tested
}

type LegacySection = "record1" | "record2" | "record3" | "additional";

interface ReadingMap {
  /** Legacy payload section. */
  src: LegacySection;
  /** Legacy value column (the *E string). */
  e: string;
  /** Legacy rating-code column (the *O number), when the reading has a pass/fail standard. */
  o?: string;
  /** Target wizard reading key. */
  key: string;
}

// High-confidence legacy column → wizard reading-key map. Only mappings with a clear 1:1
// correspondence are included; unmapped legacy columns are preserved verbatim under payload.legacy*
// by the migration and can be surfaced later. ISO step refs in comments.
const READING_MAP: ReadingMap[] = [
  // record1 — System vacuum (1), Airline drop (4), Reserve & regulation (2/3)
  { src: "record1", e: "VLVacuumReceiverE", o: "VLVacuumReceiverO", key: "tr.workingVacuum" }, // 1a
  { src: "record1", e: "VLNominalVacuumE", key: "tr.nominalVacuum" }, // 1b
  { src: "record1", e: "VLVacuumRegulationE", o: "VLVacuumRegulationO", key: "tr.regulationDeviation" }, // 1c
  { src: "record1", e: "VLVacuumRegulatorE", key: "tr.vacuumAtRegulator" }, // 1d
  { src: "record1", e: "VLVacuumPumpE", key: "tr.vacuumAtPump" }, // 1e
  { src: "record1", e: "VLVacuumVPspeedE", o: "VLVacuumVPspeedO", key: "tr.minSpeedVacuum" }, // 1f
  { src: "record1", e: "AVDVacuumReceiverE", key: "tr.airlineVacReceiver" }, // 4a
  { src: "record1", e: "AVDVacuumRegulatorE", key: "tr.airlineVacRegulator" }, // 4b
  { src: "record1", e: "AVDVacuumDropRRE", o: "AVDVacuumDropRRO", key: "tr.airlineDropRR" }, // 4c
  { src: "record1", e: "AVDVacuumPumpE", key: "tr.airlineVacPump" }, // 4d
  { src: "record1", e: "AVDVacuumPumpDropE", o: "AVDVacuumPumpDropO", key: "tr.airlinePumpDrop" }, // 4e
  { src: "record1", e: "RCEffectiveReserveE", o: "RCEffectiveReserveO", key: "tr.effectiveReserve" }, // 2a
  { src: "record1", e: "RCManualReserveE", key: "tr.manualReserve" }, // 2c
  { src: "record1", e: "RCRegulationLossE", o: "RCRegulationLossO", key: "tr.regulationLoss" }, // 2d
  { src: "record1", e: "RCRegulatorLeakageE", o: "RCRegulatorLeakageO", key: "tr.regulatorLeakage" }, // 2f
  { src: "record1", e: "RCReceiverVacuumE", key: "tr.avgReceiverVacuum" }, // 3a
  { src: "record1", e: "RCMinVacuumAirInletE", key: "tr.minVacuumAirInlet" }, // 3b
  { src: "record1", e: "RCAvgVacuumAirInletE", key: "tr.avgVacuumAirInlet" }, // 3c
  { src: "record1", e: "RCMaxVacuumIncreaseE", key: "tr.maxVacuumIncrease" }, // 3d
  { src: "record1", e: "RCAvgVacuumStopAirInletE", key: "tr.avgVacuumStopAirInlet" }, // 3e
  { src: "record1", e: "RCFallVacuumDropE", o: "RCFallVacuumDropO", key: "tr.fallOff" }, // 3f
  { src: "record1", e: "RCRegulationUndershootE", o: "RCRegulationUndershootO", key: "tr.regulationUndershoot" }, // 3g
  { src: "record1", e: "RCRegulationOvershootE", o: "RCRegulationOvershootO", key: "tr.regulationOvershoot" }, // 3h

  // record2 — Regulator sensitivity (5), Reserve off cluster (6), Gauge accuracy (7), Pumps (8/9)
  { src: "record2", e: "RSVacuumMilkingSystemE", key: "tr.regSensWorkingVac" }, // 5a
  { src: "record2", e: "RSRegulationSensitivityE", o: "RSRegulationSensitivityO", key: "tr.regulatorSensitivity" }, // 5b
  { src: "record2", e: "RVCVacuumInterceptorE", key: "tr.reserveOffClusterInterceptor" }, // 6a
  { src: "record2", e: "RVCEffectiveReserveE", key: "tr.reserveOffClusterEffective" }, // 6b
  { src: "record2", e: "VGAFarmGaugeVacuum1E", key: "tr.farmGauge1" }, // 7a
  { src: "record2", e: "VGATestGaugeVacuum1E", key: "tr.testGauge1" }, // 7b
  { src: "record2", e: "VGAGaugeError1E", o: "VGAGaugeError1O", key: "tr.gaugeError1" }, // 7c
  { src: "record2", e: "VGAFarmGaugeVacuum2E", key: "tr.farmGauge2" }, // 7d
  { src: "record2", e: "VGATestGaugeVacuum2E", key: "tr.testGauge2" }, // 7e
  { src: "record2", e: "VGAGaugeError2E", o: "VGAGaugeError2O", key: "tr.gaugeError2" }, // 7f
  { src: "record2", e: "VGAFarmGaugeVacuum3E", key: "tr.farmGauge3" }, // 7g
  { src: "record2", e: "VGATestGaugeVacuum3E", key: "tr.testGauge3" }, // 7h
  { src: "record2", e: "VGAGaugeError3E", o: "VGAGaugeError3O", key: "tr.gaugeError3" }, // 7i
  { src: "record2", e: "PPVVacuumPumpExhaustE", key: "tr.exhaustPressure" }, // 9a
  { src: "record2", e: "PPVPumpCapacityE", key: "tr.pumpCapacityTotal" }, // 9b

  // record3 — Additional leakage (10), ACR (11), Cluster air (12), Pulsator & ancillary (14), Test pulsation (15)
  { src: "record3", e: "AMSVacuumSystemLeakageE", o: "AMSVacuumSystemLeakageO", key: "add.vacuumSystemLeakage" }, // 10b
  { src: "record3", e: "AMSMilkingSystemLeakageE", o: "AMSMilkingSystemLeakageO", key: "add.milkSystemLeakage" }, // 10d
  { src: "record3", e: "ACRAirFlowE", key: "add.acrAirflow" }, // 11a
  { src: "record3", e: "ACRConsumptionE", o: "ACRConsumptionO", key: "add.acrConsumption" }, // 11b
  { src: "record3", e: "CAAClusterAirAdmissionE", o: "CAAClusterAirAdmissionO", key: "add.clusterAirAdmission" }, // 12b
  { src: "record3", e: "PAEMilkSystemAncillaryE", o: "PAEMilkSystemAncillaryO", key: "puls.milkSystemAncillary" }, // 14b
  { src: "record3", e: "PAEPulsatorConsumptionE", o: "PAEPulsatorConsumptionO", key: "puls.pulsatorConsumption" }, // 14d
  { src: "record3", e: "PAEVacuumSystemAncillaryE", o: "PAEVacuumSystemAncillaryO", key: "puls.vacuumSystemAncillary" }, // 14f
  { src: "record3", e: "TPReadingE", o: "TPReadingO", key: "puls.testPulsationReading" }, // 15b
];

// Per-pump (1–4) capacity/speed columns → tr.pumpCapacityN / tr.pumpMinSpeedN / tr.pumpMaxSpeedN (8a–c).
for (let i = 1; i <= 4; i++) {
  READING_MAP.push(
    { src: "record2", e: `VPCPumpCapacity${i}E`, o: `VPCPumpCapacity${i}O`, key: `tr.pumpCapacity${i}` },
    { src: "record2", e: `VPCPumpSpeedMinimum${i}E`, o: `VPCPumpSpeedMinimum${i}O`, key: `tr.pumpMinSpeed${i}` },
    { src: "record2", e: `VPCPumpSpeedMaximum${i}E`, o: `VPCPumpSpeedMaximum${i}O`, key: `tr.pumpMaxSpeed${i}` },
  );
}

// Section-level "fault improvement" narratives the legacy app stored on the test summary — the
// closest thing to per-test recommendations (not per-fault like the new wizard).
const SUMMARY_RECOMMENDATIONS: [column: string, label: string][] = [
  ["MMFaultImprovement", "Milking machine"],
  ["VFCFaultImprovement", "Visual faults"],
  ["MMAddFaultImprovement", "Additional tests"],
  ["PSRFaultImprovement", "Pulsation system"],
  ["ICAFaultImprovement", "Individual cluster"],
];

// Visual-fault sections. Each carries *E (int verdict 1/2/3) + *O (observation text); code 3 is a
// recorded fault and its *O column holds the human description. *E_IP columns are measured values.
const VISUAL_SECTIONS = ["visualStart", "visualRunning1", "visualRunning2", "visualRunning3", "visualRunning4"];

export interface AdaptedReadings {
  /** Numeric reading values keyed by the wizard reading key. */
  readings: Record<string, number>;
  /** As-recorded pass/fail per reading key (only where the legacy O-code gave a verdict). */
  verdicts: Record<string, LegacyVerdict>;
  /** Free-text tester comment (legacy MMComment), if any. */
  comment?: string;
  /** Section-level recommendation narratives as recorded (label + text). */
  recordedRecommendations: { label: string; text: string }[];
  /** Visual-fault observation texts as recorded (the *O text where the *E verdict = fault). */
  recordedVisualFaults: string[];
  /** Per-cluster rows (ISO 13) as recorded — total air / leakage / air-vent per unit. */
  clusterRows: MeasurementRow[];
}

/** Parse a legacy *E value (a string) to a number; returns null for blank / non-numeric. */
function num(v: unknown): number | null {
  if (v === null || v === undefined) return null;
  const s = String(v).trim();
  if (s === "" || s === "-") return null;
  const n = Number(s);
  return Number.isFinite(n) ? n : null;
}

function asInt(v: unknown): number | null {
  const n = num(v);
  return n === null ? null : Math.trunc(n);
}

/** Trim a legacy string value; null for blank. */
function text(v: unknown): string | null {
  if (v === null || v === undefined) return null;
  const s = String(v).trim();
  return s === "" ? null : s;
}

/**
 * Build the readings + as-recorded verdicts from a migrated legacy payload. Safe on partial/minimal
 * payloads (missing sections are simply skipped).
 */
export function adaptLegacyReadings(payload: Record<string, unknown> | null | undefined): AdaptedReadings {
  const readings: Record<string, number> = {};
  const verdicts: Record<string, LegacyVerdict> = {};
  if (!payload) return { readings, verdicts, recordedRecommendations: [], recordedVisualFaults: [], clusterRows: [] };

  for (const m of READING_MAP) {
    const section = payload[m.src] as Record<string, unknown> | undefined;
    if (!section) continue;

    const value = num(section[m.e]);
    if (value !== null) readings[m.key] = value;

    if (m.o) {
      const verdict = decodeReadingVerdict(asInt(section[m.o]));
      // Only record a verdict when there is a value (O=2/3 always carries a value in legacy data).
      if (verdict && value !== null) verdicts[m.key] = verdict;
    }
  }

  const record3 = payload.record3 as Record<string, unknown> | undefined;
  const commentStr = text(record3?.MMComment) ?? undefined;

  // Section-level recommendation narratives (as recorded).
  const summary = payload.summary as Record<string, unknown> | undefined;
  const recordedRecommendations: { label: string; text: string }[] = [];
  if (summary) {
    for (const [col, label] of SUMMARY_RECOMMENDATIONS) {
      const t = text(summary[col]);
      if (t) recordedRecommendations.push({ label, text: t });
    }
  }

  // Recorded visual faults: the *O observation text wherever the paired *E verdict = 3 (fault).
  const faultSet = new Set<string>();
  for (const sectionName of VISUAL_SECTIONS) {
    const section = payload[sectionName] as Record<string, unknown> | undefined;
    if (!section) continue;
    for (const key of Object.keys(section)) {
      if (!key.endsWith("E") || key.endsWith("E_IP")) continue; // verdict columns only
      if (asInt(section[key]) !== 3) continue; // 3 = fault
      const obs = text(section[`${key.slice(0, -1)}O`]);
      if (obs && obs.toUpperCase() !== "N/A") faultSet.add(obs);
    }
  }

  // Individual-cluster rows (ISO 13): one row per UnitNo with total air / leakage / air-vent.
  const clusterRows: MeasurementRow[] = [];
  const clusterAirflow = payload.clusterAirflow;
  if (Array.isArray(clusterAirflow)) {
    clusterAirflow.forEach((row, i) => {
      const r = row as Record<string, unknown>;
      const values: Record<string, string> = {};
      const total = num(r.TotalAirAdmission);
      const leak = num(r.LeakageCluster);
      const vent = num(r.AirVentAdmission);
      if (total !== null) values.totalAirAdmission = String(total);
      if (leak !== null) values.leakage = String(leak);
      if (vent !== null) values.airVent = String(vent);
      if (Object.keys(values).length === 0) return;
      clusterRows.push({ id: `legacy-cluster-${i}`, unit: text(r.UnitNo) ?? String(i + 1), values });
    });
  }

  return {
    readings, verdicts, comment: commentStr, recordedRecommendations,
    recordedVisualFaults: [...faultSet], clusterRows,
  };
}
