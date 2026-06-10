// The effective fault-observation catalog for the visual checks: the admin-managed synced rows
// (per category: wording + CMM severity + default recommendation) win; the bundled legacy lists
// + curated ratings are the offline / pre-sync fallback.
import type { FaultSeverity } from "../wizard/types";
import { FAULT_OBSERVATIONS } from "./lookups";
import {
  recommendationForObservation as fallbackRecommendation,
  severityForObservation as fallbackSeverity,
} from "./faultRatings";

export interface FaultObservationDto {
  category: string;
  name: string;
  severity: string; // Critical | Major | Minor
  recommendation?: string | null;
}

let byCategory: Map<string, FaultObservationDto[]> = new Map();

export function applyFaultCatalogOverrides(items: FaultObservationDto[]): void {
  byCategory = new Map();
  for (const item of items) {
    const list = byCategory.get(item.category);
    if (list) list.push(item);
    else byCategory.set(item.category, [item]);
  }
}

/** Test seam / reset. */
export function clearFaultCatalogOverrides(): void {
  byCategory = new Map();
}

/** The standard fault wordings for a check — synced catalog first, bundled list as fallback. */
export function observationsFor(category: string | null | undefined): string[] {
  if (!category) return [];
  const synced = byCategory.get(category);
  if (synced) return synced.map((o) => o.name);
  return FAULT_OBSERVATIONS[category] ?? [];
}

const VALID_SEVERITIES = new Set(["Critical", "Major", "Minor"]);

/** CMM severity for a chosen observation — the synced row's severity wins. */
export function severityFor(category: string | null | undefined, observation: string | null | undefined): FaultSeverity {
  if (category && observation) {
    const row = byCategory.get(category)?.find((o) => o.name === observation);
    if (row && VALID_SEVERITIES.has(row.severity)) return row.severity as FaultSeverity;
  }
  return fallbackSeverity(observation);
}

/** Default recommendation for a chosen observation — the synced row's wording wins. */
export function recommendationFor(category: string | null | undefined, observation: string | null | undefined): string {
  if (category && observation) {
    const row = byCategory.get(category)?.find((o) => o.name === observation);
    if (row?.recommendation) return row.recommendation;
  }
  return fallbackRecommendation(observation);
}
