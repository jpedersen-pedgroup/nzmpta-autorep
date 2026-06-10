// TypeScript mirror of Domain/PassFail/PassFailCalculator.cs. Pure: (measurement, rule) -> verdict,
// used for the wizard's live pass/fail indicators. Pinned by the shared fixtures in
// tests/fixtures/passfail (the same JSON the .NET xUnit suite uses).

export type PassFailVerdict = "pass" | "fail" | "noStandard";

/**
 * A pass/fail threshold for one reading. Flat shape (optional fields) so the JSON matches both
 * implementations. Kinds: atMost (<= limit), atLeast (>= min), between (min..max inclusive),
 * tolerance (|value - target| <= tolerance), none.
 */
export interface PassFailRule {
  kind: "atMost" | "atLeast" | "between" | "tolerance" | "none";
  limit?: number;
  min?: number;
  max?: number;
  target?: number;
  tolerance?: number;
}

const verdict = (pass: boolean): PassFailVerdict => (pass ? "pass" : "fail");

export function evaluate(value: number | null | undefined, rule: PassFailRule): PassFailVerdict {
  if (rule.kind === "none") return "noStandard";
  if (value === null || value === undefined || Number.isNaN(value)) return "noStandard";

  switch (rule.kind) {
    case "atMost":
      return rule.limit == null ? "noStandard" : verdict(value <= rule.limit);
    case "atLeast":
      return rule.min == null ? "noStandard" : verdict(value >= rule.min);
    case "between":
      return rule.min == null || rule.max == null ? "noStandard" : verdict(value >= rule.min && value <= rule.max);
    case "tolerance":
      return rule.target == null || rule.tolerance == null
        ? "noStandard"
        : verdict(Math.abs(value - rule.target) <= rule.tolerance);
    default:
      return "noStandard";
  }
}
