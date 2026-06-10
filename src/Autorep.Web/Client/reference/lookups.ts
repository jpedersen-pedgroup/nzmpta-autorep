// Reference option lists for the Machine Configuration step, pulled from the legacy Autorep_bak
// `Lookup`, `AtmosPressure` and `Pulsator` tables (9 Jun 2026). Embedded for the offline PWA;
// later these migrate to admin-managed reference data synced into IndexedDB. Values are the
// legacy display names so testers see familiar choices.
import shellsJson from "./shells.json";
import linersJson from "./liners.json";
import pulsatorsJson from "./pulsators.json";
import faultObservationsJson from "./faultObservations.json";

/** Standard fault observations per visual check (legacy Lookup, keyed by Category). When a check
 * is marked Fault the Tester picks which standard fault applies. */
export const FAULT_OBSERVATIONS: Record<string, string[]> = faultObservationsJson;
export function faultObservationsFor(category: string | null | undefined): string[] {
  if (!category) return [];
  return FAULT_OBSERVATIONS[category] ?? [];
}

export const MILKLINE_SIZES = ["50", "63", "75", "100"] as const;

/** Lookup category 'PulsatorSize' — the pulsator configuration (e.g. 2×2, 4+0). */
export const PULSATOR_CONFIGS = ["2 X 2", "4 + 0"] as const;

export const SYSTEM_COUNTS = [1, 2, 3, 4, 5] as const;

export interface AtmosPressureOption {
  /** Prevailing atmospheric pressure at the test site (kPa). */
  kpa: number;
  /** Correction factor: MULTIPLY the measured airflow (effective reserve, pump capacity) by this
   * before comparing to the standard — manual p31 / ISO 6690 §5.3.2. */
  cFactor: number;
}
// 90–100 kPa rows verified against the manual p31 altitude table; 101–105 against ISO 6690
// Table 4 (K2 @ 50 kPa: 103→0.96, interpolated between the 100/103/106 anchor rows). The legacy
// app's 102→0.98 / 103→0.97 were off by one step vs ISO.
export const ATMOS_PRESSURES: AtmosPressureOption[] = [
  { kpa: 90, cFactor: 1.16 },
  { kpa: 91, cFactor: 1.14 },
  { kpa: 92, cFactor: 1.12 },
  { kpa: 93, cFactor: 1.1 },
  { kpa: 94, cFactor: 1.09 },
  { kpa: 95, cFactor: 1.07 },
  { kpa: 96, cFactor: 1.05 },
  { kpa: 97, cFactor: 1.04 },
  { kpa: 98, cFactor: 1.03 },
  { kpa: 99, cFactor: 1.01 },
  { kpa: 100, cFactor: 1 },
  { kpa: 101, cFactor: 0.99 },
  { kpa: 102, cFactor: 0.97 },
  { kpa: 103, cFactor: 0.96 },
  { kpa: 104, cFactor: 0.95 },
  { kpa: 105, cFactor: 0.94 },
];

export function correctionFactorFor(kpa: number | null | undefined): number | null {
  if (kpa == null) return null;
  return ATMOS_PRESSURES.find((a) => a.kpa === kpa)?.cFactor ?? null;
}

export const SHELLS: string[] = shellsJson;
export const LINERS: string[] = linersJson;

export interface PulsatorOption {
  name: string;
  brand: string;
}
export const PULSATORS: PulsatorOption[] = pulsatorsJson;

/** Distinct pulsator brands (Lookup-backed in legacy via Pulsator.PULBrand). */
export const PULSATOR_BRANDS: string[] = [...new Set(PULSATORS.map((p) => p.brand))].sort((a, b) =>
  a.localeCompare(b),
);

/** Pulsator models for a brand (for the dependent Type dropdown). */
export function pulsatorModelsForBrand(brand: string | null | undefined): string[] {
  if (!brand) return [];
  return PULSATORS.filter((p) => p.brand === brand).map((p) => p.name);
}
