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

/** Test Record readings (ISO groups 1–9, first cut) with their pass/fail rules. */
export function testRecordSections(config: MachineConfiguration): ReadingSection[] {
  const reqReserve = requiredEffectiveReserve(config.clusterCount);

  return [
    {
      key: "SystemVacuumLevels",
      title: "System vacuum",
      readings: [
        { key: "tr.workingVacuum", label: "Working vacuum @ receiver (1a)", unit: "kPa", rule: { kind: "between", min: 40, max: 50 } },
        { key: "tr.regulationDeviation", label: "Vacuum regulation deviation (1c)", unit: "kPa", rule: { kind: "atMost", limit: 2 } },
      ],
    },
    {
      key: "ReserveCharacteristics",
      title: "Reserve",
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
      ],
    },
    {
      key: "VacuumGaugeAccuracy",
      title: "Gauge accuracy",
      readings: [
        { key: "tr.gaugeError", label: "Farm gauge error @ working vacuum (7)", unit: "kPa", rule: { kind: "atMost", limit: 1 } },
      ],
    },
  ];
}

/** Additional Tests (ISO 10–16) — sections gated by the machine's ancillaries, mirroring the
 * resolver. Cluster air admission is config-driven (vented liners widen the band). */
export function additionalTestSections(config: MachineConfiguration): ReadingSection[] {
  const sections: ReadingSection[] = [
    {
      key: "AirlineMilkSystemLeakage",
      title: "Airline & milk leakage",
      readings: [
        { key: "add.airlineDrop", label: "Airline vacuum drop (10)", unit: "kPa", rule: { kind: "atMost", limit: 2 } },
        { key: "add.milkSystemLeak", label: "Milk system leakage", unit: "L/min", rule: { kind: "none" } },
      ],
    },
  ];
  if (config.hasAcr) {
    sections.push({ key: "AcrConsumption", title: "ACR", readings: [
      { key: "add.acrConsumption", label: "ACR consumption (11)", unit: "L/min", rule: { kind: "none" } },
    ] });
  }
  sections.push({
    key: "ClusterAirAdmission",
    title: "Cluster air admission",
    readings: [
      {
        key: "add.clusterAirAdmission",
        label: "Cluster air admission per cluster (12)",
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
  sections.push({ key: "RegulatorLoad", title: "Regulator", readings: [
    { key: "add.regulatorLoad", label: "Peak regulator load (14)", unit: "kPa", rule: { kind: "atMost", limit: 2 } },
  ] });
  return sections;
}

/** Pulsator Test Results — summary rates/ratios + airline stability. (Per-pulsator rows are a
 * follow-up; this captures the summary values the standard checks.) */
export function pulsatorSections(_config: MachineConfiguration): ReadingSection[] {
  return [
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

/** Individual Cluster Tests (optional) — simplified single-entry first cut (per-cluster rows
 * are a follow-up). */
export function individualClusterSections(_config: MachineConfiguration): ReadingSection[] {
  return [
    {
      key: "IndividualCluster",
      title: "Cluster airflow",
      readings: [
        { key: "ica.totalAirAdmission", label: "Total cluster air admission", unit: "L/min", rule: { kind: "none" } },
        { key: "ica.leakage", label: "Cluster leakage", unit: "L/min", rule: { kind: "none" } },
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
