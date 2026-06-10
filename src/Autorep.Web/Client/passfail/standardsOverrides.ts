// Admin-managed standard overrides, synced from /api/standards. The formulas live in code
// (standards.ts / pulsatorStats.ts); every number in them resolves through here — a synced row
// wins, the built-in default (verified against the manual/ISO) is the fallback, so the wizard
// works offline and before the first sync.
import type { PassFailRule } from "./passFail";

export interface StandardDto {
  key: string;
  label: string;
  category: string;
  kind: string; // atMost | atLeast | between | tolerance | param
  limit?: number | null;
  min?: number | null;
  max?: number | null;
  target?: number | null;
  tolerance?: number | null;
  value?: number | null;
  unit?: string | null;
  sourceRef?: string | null;
}

let overrides: Map<string, StandardDto> = new Map();

export function applyStandardsOverrides(rows: StandardDto[]): void {
  overrides = new Map(rows.map((r) => [r.key, r]));
}

/** Test seam / reset. */
export function clearStandardsOverrides(): void {
  overrides = new Map();
}

/** The rule for a reading key: the synced override if present and well-formed, else the default. */
export function ruleFor(key: string, fallback: PassFailRule): PassFailRule {
  const o = overrides.get(key);
  if (!o) return fallback;
  switch (o.kind) {
    case "atMost":
      return o.limit != null ? { kind: "atMost", limit: o.limit } : fallback;
    case "atLeast":
      return o.min != null ? { kind: "atLeast", min: o.min } : fallback;
    case "between":
      return o.min != null && o.max != null ? { kind: "between", min: o.min, max: o.max } : fallback;
    case "tolerance":
      return o.tolerance != null ? { kind: "tolerance", target: o.target ?? 0, tolerance: o.tolerance } : fallback;
    default:
      return fallback;
  }
}

/** A formula parameter ("param.*" key): the synced value if present, else the default. */
export function paramFor(key: string, fallback: number): number {
  const o = overrides.get(key);
  return o?.kind === "param" && o.value != null ? o.value : fallback;
}
