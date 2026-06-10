// Test-Record reading definitions + the standards that drive their pass/fail rules. The
// effective-reserve threshold is config-driven (from the cluster count, via the legacy
// EffectiveArea table). Later phases move these standards to admin-managed reference data
// synced into IndexedDB; for now they're embedded for the offline live indicators.
import type { MachineConfiguration } from "../wizard/types";
import type { PassFailRule } from "./passFail";

// EffectiveArea (Autorep_bak): required Effective Reserve (L/min) by cluster count.
// Index 0 = 1 cluster. Required airflow consumption follows 30 * ceil(clusters / 10).
const EFFECTIVE_RESERVE: number[] = [
  260, 260, 320, 320, 380, 380, 440, 440, 500, 500,
  520, 520, 540, 540, 560, 560, 580, 580, 600, 600,
  650, 650, 700, 700, 750, 750, 800, 800, 850, 850,
  900, 900, 950, 950, 1000, 1000, 1050, 1050, 1100, 1100,
  1150, 1150, 1200, 1200, 1250, 1250, 1300, 1300, 1350, 1350,
  1400, 1400, 1450, 1450, 1500, 1500, 1550, 1550, 1600, 1600,
  1650, 1650, 1700, 1700, 1750, 1750, 1800, 1800, 1850, 1850,
  1900, 1900, 1950, 1950, 2000, 2000, 2050, 2050, 2100, 2100,
  2150, 2150, 2200, 2200, 2250, 2250, 2300, 2300, 2350, 2350,
  2400, 2400, 2450, 2450, 2500, 2500, 2550, 2550, 2600, 2600,
];

export function requiredEffectiveReserve(clusters: number): number | null {
  if (clusters < 1) return null;
  return EFFECTIVE_RESERVE[Math.min(clusters, EFFECTIVE_RESERVE.length) - 1] ?? null;
}

export function requiredAirflow(clusters: number): number | null {
  if (clusters < 1) return null;
  return 30 * Math.ceil(Math.min(clusters, 100) / 10);
}

export interface ReadingDef {
  key: string;
  label: string;
  unit: string;
  rule: PassFailRule;
  hint?: string;
}
export interface ReadingSection {
  key: string;
  title: string;
  readings: ReadingDef[];
}

/** Test Record readings — the full ISO numerical workflow groups 1–9, each reading tagged with its
 * flowchart ref (1a…9b). Live pass/fail only where the standard is certain; the rest are capture-only
 * until the thresholds are confirmed. Section keys mirror the WizardStepResolver. */
export function testRecordSections(config: MachineConfiguration): ReadingSection[] {
  const reqReserve = requiredEffectiveReserve(config.clusterCount);
  const sections: ReadingSection[] = [];

  // 1 — System vacuum levels
  sections.push({
    key: "SystemVacuumLevels",
    title: "1 · System vacuum",
    readings: [
      { key: "tr.workingVacuum", label: "Working vacuum @ receiver (1a)", unit: "kPa", rule: { kind: "between", min: 40, max: 50 } },
      { key: "tr.nominalVacuum", label: "Nominal vacuum (1b)", unit: "kPa", rule: { kind: "none" } },
      { key: "tr.regulationDeviation", label: "Vacuum regulation deviation (1c)", unit: "kPa", rule: { kind: "atMost", limit: 2 } },
      { key: "tr.vacuumAtRegulator", label: "Vacuum @ regulator (1d)", unit: "kPa", rule: { kind: "none" } },
      { key: "tr.vacuumAtPump", label: "Vacuum @ pump (1e)", unit: "kPa", rule: { kind: "none" } },
    ],
  });

  // 1f — Minimum pump-speed vacuum (VSD only)
  if (config.vsdFitted) {
    sections.push({
      key: "MinPumpSpeedVacuum",
      title: "1 · Min pump-speed vacuum",
      readings: [
        { key: "tr.minSpeedVacuum", label: "Vacuum @ minimum VSD speed (1f)", unit: "kPa", rule: { kind: "none" } },
      ],
    });
  }

  // 2 — Reserve characteristics
  sections.push({
    key: "ReserveCharacteristics",
    title: "2 · Reserve",
    readings: [
      {
        key: "tr.effectiveReserve",
        label: "Effective reserve (2a)",
        unit: "L/min",
        hint:
          reqReserve != null
            ? `needs ≥ ${reqReserve} for ${config.clusterCount} clusters`
            : "set the cluster count for the standard",
        rule: reqReserve != null ? { kind: "atLeast", min: reqReserve } : { kind: "none" },
      },
      { key: "tr.reserveAirflow", label: "Airflow (2b)", unit: "L/min", rule: { kind: "none" } },
      { key: "tr.manualReserve", label: "Manual reserve (2c)", unit: "L/min", rule: { kind: "none" } },
      { key: "tr.regulationLoss", label: "Regulation loss (2d)", unit: "L/min", rule: { kind: "none" } },
      { key: "tr.regulatorLeakageAirflow", label: "Airflow at regulator leakage (2e)", unit: "L/min", rule: { kind: "none" } },
      { key: "tr.regulatorLeakage", label: "Regulator leakage (2f)", unit: "L/min", rule: { kind: "none" } },
    ],
  });

  // 3 — Regulation characteristics
  sections.push({
    key: "RegulationCharacteristics",
    title: "3 · Regulation",
    readings: [
      { key: "tr.avgReceiverVacuum", label: "Average receiver vacuum (3a)", unit: "kPa", rule: { kind: "none" } },
      { key: "tr.minVacuumAirInlet", label: "Min vacuum at air inlet (3b)", unit: "kPa", rule: { kind: "none" } },
      { key: "tr.avgVacuumAirInlet", label: "Avg vacuum at air inlet (3c)", unit: "kPa", rule: { kind: "none" } },
      { key: "tr.maxVacuumIncrease", label: "Max vacuum increase (3d)", unit: "kPa", rule: { kind: "none" } },
      { key: "tr.avgVacuumStopAirInlet", label: "Avg vacuum, air inlet stopped (3e)", unit: "kPa", rule: { kind: "none" } },
      { key: "tr.fallOff", label: "Fall-off (3f)", unit: "kPa", rule: { kind: "atMost", limit: 2 } },
      { key: "tr.regulationUndershoot", label: "Regulation undershoot (3g)", unit: "kPa", rule: { kind: "atMost", limit: 2 } },
      { key: "tr.regulationOvershoot", label: "Regulation overshoot (3h)", unit: "kPa", rule: { kind: "atMost", limit: 2 } },
    ],
  });

  // 4 — Vacuum drop in airline
  sections.push({
    key: "VacuumDropAirline",
    title: "4 · Airline drop",
    readings: [
      { key: "tr.airlineVacReceiver", label: "Vacuum @ receiver (4a)", unit: "kPa", rule: { kind: "none" } },
      { key: "tr.airlineVacRegulator", label: "Vacuum @ regulator (4b)", unit: "kPa", rule: { kind: "none" } },
      { key: "tr.airlineDropRR", label: "Drop receiver → regulator (4c)", unit: "kPa", rule: { kind: "atMost", limit: 2 } },
      { key: "tr.airlineVacPump", label: "Vacuum @ pump (4d)", unit: "kPa", rule: { kind: "none" } },
      { key: "tr.airlinePumpDrop", label: "Pump vacuum drop (4e)", unit: "kPa", rule: { kind: "atMost", limit: 2 } },
    ],
  });

  // 5 — Regulator sensitivity
  sections.push({
    key: "RegulatorSensitivity",
    title: "5 · Regulator sensitivity",
    readings: [
      { key: "tr.regSensWorkingVac", label: "Working vacuum in milk system (5a)", unit: "kPa", rule: { kind: "none" } },
      { key: "tr.regulatorSensitivity", label: "Regulator sensitivity (5b)", unit: "kPa", rule: { kind: "none" } },
    ],
  });

  // 6 — Reserve (vacuum off cluster) — optional
  sections.push({
    key: "ReserveVacuumOffCluster",
    title: "6 · Reserve off cluster",
    readings: [
      { key: "tr.reserveOffClusterInterceptor", label: "Vacuum at interceptor (6a)", unit: "kPa", rule: { kind: "none" } },
      { key: "tr.reserveOffClusterEffective", label: "Effective reserve off cluster (6b)", unit: "L/min", rule: { kind: "none" } },
    ],
  });

  // 7 — Vacuum gauge accuracy (3 points; farm vs test gauge, error ≤ 1 kPa)
  sections.push({
    key: "VacuumGaugeAccuracy",
    title: "7 · Gauge accuracy",
    readings: [
      { key: "tr.farmGauge1", label: "Farm gauge — point 1 (7a)", unit: "kPa", rule: { kind: "none" } },
      { key: "tr.testGauge1", label: "Test gauge — point 1 (7b)", unit: "kPa", rule: { kind: "none" } },
      { key: "tr.gaugeError1", label: "Gauge error — point 1 (7c)", unit: "kPa", rule: { kind: "atMost", limit: 1 } },
      { key: "tr.farmGauge2", label: "Farm gauge — point 2 (7d)", unit: "kPa", rule: { kind: "none" } },
      { key: "tr.testGauge2", label: "Test gauge — point 2 (7e)", unit: "kPa", rule: { kind: "none" } },
      { key: "tr.gaugeError2", label: "Gauge error — point 2 (7f)", unit: "kPa", rule: { kind: "atMost", limit: 1 } },
      { key: "tr.farmGauge3", label: "Farm gauge — point 3 (7g)", unit: "kPa", rule: { kind: "none" } },
      { key: "tr.testGauge3", label: "Test gauge — point 3 (7h)", unit: "kPa", rule: { kind: "none" } },
      { key: "tr.gaugeError3", label: "Gauge error — point 3 (7i)", unit: "kPa", rule: { kind: "atMost", limit: 1 } },
    ],
  });

  // 8 — Vacuum pump test (per pump: capacity @50kPa, min speed, speed @50kPa)
  const pumpReadings: ReadingDef[] = [];
  const pumps = Math.max(1, config.numberOfVacuumPumps);
  for (let i = 1; i <= pumps; i++) {
    const p = pumps > 1 ? ` — pump ${i}` : "";
    pumpReadings.push(
      { key: `tr.pumpCapacity${i}`, label: `Capacity @ 50 kPa (8a)${p}`, unit: "L/min", rule: { kind: "none" } },
      { key: `tr.pumpMinSpeed${i}`, label: `Minimum speed (8b)${p}`, unit: "rpm", rule: { kind: "none" } },
      { key: `tr.pumpMaxSpeed${i}`, label: `Speed @ 50 kPa (8c)${p}`, unit: "rpm", rule: { kind: "none" } },
    );
  }
  sections.push({ key: "VacuumPumpTest", title: "8 · Vacuum pump(s)", readings: pumpReadings });

  // 9 — Vacuum pump exhaust pressure
  sections.push({
    key: "PumpExhaustPressure",
    title: "9 · Pump exhaust",
    readings: [
      { key: "tr.exhaustPressure", label: "Exhaust pressure (9a)", unit: "kPa", rule: { kind: "none" } },
      { key: "tr.pumpCapacityTotal", label: "Pump capacity (9b)", unit: "L/min", rule: { kind: "none" } },
    ],
  });

  return sections;
}

/** Additional Tests (ISO 10–16) — sections gated by the machine's ancillaries, mirroring the
 * resolver. Cluster air admission is config-driven (vented liners widen the band). */
export function additionalTestSections(config: MachineConfiguration): ReadingSection[] {
  const sections: ReadingSection[] = [
    {
      key: "AirlineMilkSystemLeakage",
      title: "10 · Airline & milk leakage",
      readings: [
        { key: "add.airflowVacuumSystem", label: "Airflow — vacuum system (10a)", unit: "L/min", rule: { kind: "none" } },
        { key: "add.vacuumSystemLeakage", label: "Vacuum system leakage (10b)", unit: "L/min", rule: { kind: "none" } },
        { key: "add.airflowMilkSystem", label: "Airflow — milk system (10c)", unit: "L/min", rule: { kind: "none" } },
        { key: "add.milkSystemLeakage", label: "Milk system leakage (10d)", unit: "L/min", rule: { kind: "none" } },
      ],
    },
  ];
  if (config.hasAcr) {
    sections.push({ key: "AcrConsumption", title: "11 · ACR", readings: [
      { key: "add.acrAirflow", label: "ACR airflow (11a)", unit: "L/min", rule: { kind: "none" } },
      { key: "add.acrConsumption", label: "ACR consumption (11b)", unit: "L/min", rule: { kind: "none" } },
    ] });
  }
  sections.push({
    key: "ClusterAirAdmission",
    title: "12 · Cluster air admission",
    readings: [
      { key: "add.clusterAirAdmissionConnect", label: "Connected airflow (12a)", unit: "L/min", rule: { kind: "none" } },
      {
        key: "add.clusterAirAdmission",
        label: "Cluster air admission per cluster (12b)",
        unit: "L/min",
        hint: config.linerVented ? "≤ 35 (vented liners)" : "4–12 per cluster",
        rule: config.linerVented ? { kind: "atMost", limit: 35 } : { kind: "between", min: 4, max: 12 },
      },
    ],
  });
  if (config.hasMilkMeters) {
    sections.push({ key: "MilkMeter", title: "Milk meters", readings: [
      { key: "add.milkMeter", label: "Milk meter consumption", unit: "L/min", rule: { kind: "none" } },
    ] });
  }
  if (config.hasTeatSprayer) {
    sections.push({ key: "TeatSpray", title: "Teat sprayer", readings: [
      { key: "add.teatSpray", label: "Teat sprayer consumption", unit: "L/min", rule: { kind: "none" } },
    ] });
  }
  if (config.hasBailGates || config.hasBackingGate) {
    sections.push({ key: "GateCylinder", title: "Gates", readings: [
      { key: "add.gateCylinder", label: "Gate cylinder consumption", unit: "L/min", rule: { kind: "none" } },
    ] });
  }
  if (config.hasReleaserPump) {
    sections.push({ key: "ReleaserPumpHeads", title: "Releaser pump", readings: [
      { key: "add.releaserSpeed", label: "Releaser pump speed", unit: "rpm", rule: { kind: "none" } },
      { key: "add.releaserPower", label: "Releaser pump power", unit: "kW", rule: { kind: "none" } },
    ] });
  }
  sections.push({ key: "RegulatorLoad", title: "Peak regulator load", readings: [
    { key: "add.regulatorLoad", label: "Peak regulator load", unit: "kPa", rule: { kind: "atMost", limit: 2 } },
  ] });
  return sections;
}

/** Pulsator Test Results — ISO 14 (pulsator & ancillary air consumption) + 15 (test pulsation),
 * plus the summary rates/ratios the standard checks. (Per-pulsator row table is a follow-up.) */
export function pulsatorSections(_config: MachineConfiguration): ReadingSection[] {
  return [
    {
      key: "PulsatorAncillary",
      title: "14 · Pulsator & ancillary",
      readings: [
        { key: "puls.airflowMilkSystem", label: "Airflow — milk system (14a)", unit: "L/min", rule: { kind: "none" } },
        { key: "puls.milkSystemAncillary", label: "Milk-system ancillary consumption (14b)", unit: "L/min", rule: { kind: "none" } },
        { key: "puls.airflowPulsators", label: "Airflow — pulsators (14c)", unit: "L/min", rule: { kind: "none" } },
        { key: "puls.pulsatorConsumption", label: "Pulsator consumption (14d)", unit: "L/min", rule: { kind: "none" } },
        { key: "puls.airflowVacuumSystem", label: "Airflow — vacuum system (14e)", unit: "L/min", rule: { kind: "none" } },
        { key: "puls.vacuumSystemAncillary", label: "Vacuum-system ancillary consumption (14f)", unit: "L/min", rule: { kind: "none" } },
      ],
    },
    {
      key: "TestPulsation",
      title: "15 · Test pulsation",
      readings: [
        { key: "puls.maxChamberVacuum", label: "Max pulsation chamber vacuum, B phase (15a)", unit: "kPa", rule: { kind: "none" } },
        { key: "puls.testPulsationReading", label: "Test pulsation reading (15b)", unit: "kPa", rule: { kind: "none" } },
      ],
    },
    {
      key: "PulsatorRates",
      title: "Rates & ratios",
      readings: [
        { key: "puls.fastestRate", label: "Fastest pulsator rate", unit: "ppm", rule: { kind: "none" } },
        { key: "puls.slowestRate", label: "Slowest pulsator rate", unit: "ppm", rule: { kind: "none" } },
        { key: "puls.highestRatio", label: "Highest ratio", unit: "%", rule: { kind: "none" } },
        { key: "puls.lowestRatio", label: "Lowest ratio", unit: "%", rule: { kind: "none" } },
      ],
    },
    {
      key: "PulsatorStability",
      title: "Stability",
      readings: [
        { key: "puls.airlineStability", label: "Pulsator airline stability", unit: "kPa", rule: { kind: "atMost", limit: 4 } },
      ],
    },
  ];
}

/** Individual Cluster Tests (optional) — ISO 13 (per-cluster air admission). Summary capture for
 * now; the per-cluster row table is a follow-up. */
export function individualClusterSections(_config: MachineConfiguration): ReadingSection[] {
  return [
    {
      key: "IndividualCluster",
      title: "13 · Individual cluster",
      readings: [
        { key: "ica.totalAirAdmission", label: "Total cluster air admission (13a)", unit: "L/min", rule: { kind: "none" } },
        { key: "ica.leakage", label: "Cluster leakage (13b)", unit: "L/min", rule: { kind: "none" } },
        { key: "ica.airVentAdmission", label: "Air-vent admission (13c)", unit: "L/min", rule: { kind: "none" } },
      ],
    },
  ];
}

/** All numerical reading sections across the reading-based steps — used to build the fault list. */
export function allReadingSections(config: MachineConfiguration): ReadingSection[] {
  return [
    ...testRecordSections(config),
    ...additionalTestSections(config),
    ...pulsatorSections(config),
    ...individualClusterSections(config),
  ];
}
