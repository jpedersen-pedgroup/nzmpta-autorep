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
