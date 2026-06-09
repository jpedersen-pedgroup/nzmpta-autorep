// TypeScript mirror of Domain/Faults/FaultAggregator.cs. Pure: a list of faults -> a FaultSummary
// grouped by component (each group rated by its worst fault, worst-severity-first). Pinned by the
// shared fixtures in tests/fixtures/faults (the same JSON the .NET xUnit suite uses).
import type { FaultSeverity } from "../wizard/types";

export interface FaultInput {
  /** Stable key (the source visual-item / reading key) — used to attach recommendations. */
  key?: string;
  component: string;
  description: string;
  severity: FaultSeverity;
  source: string;
  recommendation?: string;
}

export interface FaultGroup {
  component: string;
  severity: FaultSeverity;
  faults: FaultInput[];
}

export interface FaultSummary {
  groups: FaultGroup[];
  critical: number;
  major: number;
  minor: number;
  total: number;
}

const RANK: Record<FaultSeverity, number> = { Critical: 3, Major: 2, Minor: 1 };

export function aggregate(faults: FaultInput[]): FaultSummary {
  const byComponent = new Map<string, FaultInput[]>();
  for (const f of faults) {
    const list = byComponent.get(f.component) ?? [];
    list.push(f);
    byComponent.set(f.component, list);
  }

  const groups: FaultGroup[] = [...byComponent.entries()].map(([component, list]) => ({
    component,
    severity: list.reduce<FaultSeverity>((worst, f) => (RANK[f.severity] > RANK[worst] ? f.severity : worst), "Minor"),
    faults: list,
  }));
  groups.sort((a, b) => RANK[b.severity] - RANK[a.severity] || (a.component < b.component ? -1 : a.component > b.component ? 1 : 0));

  const count = (s: FaultSeverity) => faults.filter((f) => f.severity === s).length;
  return { groups, critical: count("Critical"), major: count("Major"), minor: count("Minor"), total: faults.length };
}
