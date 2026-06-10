// Pulsator summary stats computed from the per-pulsator rows. The required ISO checks are the
// rate spread (fastest − slowest ≤ 6 ppm) and ratio spread (highest − lowest ≤ 5%); both are
// model-independent. (The per-model rate/ratio band check, from the Pulsator catalog, is a
// follow-up.)
import type { MeasurementRow } from "../db/testStore";

export const RATE_SPREAD_MAX = 6; // ppm
export const RATIO_SPREAD_MAX = 5; // %

export interface PulsatorSummary {
  fastestRate: number | null;
  slowestRate: number | null;
  highestRatio: number | null;
  lowestRatio: number | null;
  rateSpread: number | null;
  ratioSpread: number | null;
  rateSpreadOk: boolean | null;
  ratioSpreadOk: boolean | null;
}

function nums(values: (string | undefined)[]): number[] {
  return values
    .map((v) => (v == null || v.trim() === "" ? NaN : Number(v)))
    .filter((n) => !Number.isNaN(n));
}

export function pulsatorSummary(rows: MeasurementRow[]): PulsatorSummary {
  const rates = nums(rows.map((r) => r.values.rate));
  const ratios = nums(rows.flatMap((r) => [r.values.ratioFront, r.values.ratioBack]));

  const fastestRate = rates.length ? Math.max(...rates) : null;
  const slowestRate = rates.length ? Math.min(...rates) : null;
  const highestRatio = ratios.length ? Math.max(...ratios) : null;
  const lowestRatio = ratios.length ? Math.min(...ratios) : null;

  const rateSpread = fastestRate != null && slowestRate != null ? fastestRate - slowestRate : null;
  const ratioSpread = highestRatio != null && lowestRatio != null ? highestRatio - lowestRatio : null;

  return {
    fastestRate,
    slowestRate,
    highestRatio,
    lowestRatio,
    rateSpread,
    ratioSpread,
    rateSpreadOk: rateSpread == null ? null : rateSpread <= RATE_SPREAD_MAX,
    ratioSpreadOk: ratioSpread == null ? null : ratioSpread <= RATIO_SPREAD_MAX,
  };
}
