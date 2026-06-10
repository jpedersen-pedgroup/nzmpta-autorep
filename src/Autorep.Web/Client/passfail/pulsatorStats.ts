// Pulsator summary stats computed from the per-pulsator rows. Standards (manual pp49–53 /
// ISO 6690 Table D.5): rate spread ≤ 6 ppm between fastest and slowest pulsator; ratio variation
// ≤ 5% BETWEEN pulsators — compared within the same quarter group (front vs front, back vs back),
// because front and back quarters may run different ratios by design; limping ≤ 5% within a
// cluster (taken from the analyser's per-row limp value). Per-model rate/ratio bands from the
// Pulsator catalog are a follow-up.
import type { MeasurementRow } from "../db/testStore";
import { paramFor } from "./standardsOverrides";

// Built-in defaults; the synced admin-managed standards override them (param.pulsation.*).
export const RATE_SPREAD_MAX = 6; // ppm
export const RATIO_SPREAD_MAX = 5; // % — between pulsators, per quarter group
export const LIMP_MAX = 5; // % — within a cluster

/** The effective pulsation limits (synced overrides applied). */
export function pulsationLimits(): { rateSpreadMax: number; ratioSpreadMax: number; limpMax: number } {
  return {
    rateSpreadMax: paramFor("param.pulsation.rateSpreadMax", RATE_SPREAD_MAX),
    ratioSpreadMax: paramFor("param.pulsation.ratioSpreadMax", RATIO_SPREAD_MAX),
    limpMax: paramFor("param.pulsation.limpMax", LIMP_MAX),
  };
}

export interface PulsatorSummary {
  fastestRate: number | null;
  slowestRate: number | null;
  highestRatio: number | null;
  lowestRatio: number | null;
  rateSpread: number | null;
  /** Worst spread across the front group and the back group (not pooled across groups). */
  ratioSpread: number | null;
  worstLimp: number | null;
  rateSpreadOk: boolean | null;
  ratioSpreadOk: boolean | null;
  limpOk: boolean | null;
}

function nums(values: (string | undefined)[]): number[] {
  return values
    .map((v) => (v == null || v.trim() === "" ? NaN : Number(v)))
    .filter((n) => !Number.isNaN(n));
}

const spread = (ns: number[]): number | null => (ns.length ? Math.max(...ns) - Math.min(...ns) : null);

export function pulsatorSummary(rows: MeasurementRow[]): PulsatorSummary {
  const rates = nums(rows.map((r) => r.values.rate));
  const fronts = nums(rows.map((r) => r.values.ratioFront));
  const backs = nums(rows.map((r) => r.values.ratioBack));
  const allRatios = [...fronts, ...backs];
  const limps = nums(rows.map((r) => r.values.limp));

  const fastestRate = rates.length ? Math.max(...rates) : null;
  const slowestRate = rates.length ? Math.min(...rates) : null;
  const highestRatio = allRatios.length ? Math.max(...allRatios) : null;
  const lowestRatio = allRatios.length ? Math.min(...allRatios) : null;

  const rateSpread = spread(rates);
  const groupSpreads = [spread(fronts), spread(backs)].filter((s): s is number => s != null);
  const ratioSpread = groupSpreads.length ? Math.max(...groupSpreads) : null;
  const worstLimp = limps.length ? Math.max(...limps) : null;
  const limits = pulsationLimits();

  return {
    fastestRate,
    slowestRate,
    highestRatio,
    lowestRatio,
    rateSpread,
    ratioSpread,
    worstLimp,
    rateSpreadOk: rateSpread == null ? null : rateSpread <= limits.rateSpreadMax,
    ratioSpreadOk: ratioSpread == null ? null : ratioSpread <= limits.ratioSpreadMax,
    limpOk: worstLimp == null ? null : worstLimp <= limits.limpMax,
  };
}
