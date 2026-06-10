namespace Autorep.Web.Domain.PassFail;

public enum PassFailVerdict
{
    Pass,
    Fail,
    /// <summary>No applicable standard (or no reading) — show the value without a verdict.</summary>
    NoStandard,
}

/// <summary>
/// A pass/fail threshold for a single numerical reading. Flat shape (one record, optional fields)
/// so the same JSON drives the .NET and TypeScript implementations.
/// Kinds: <c>atMost</c> (≤ Limit), <c>atLeast</c> (≥ Min), <c>between</c> (Min..Max inclusive),
/// <c>tolerance</c> (|value − Target| ≤ Tolerance), <c>none</c>.
/// </summary>
public sealed record PassFailRule(
    string Kind,
    double? Limit = null,
    double? Min = null,
    double? Max = null,
    double? Target = null,
    double? Tolerance = null);

/// <summary>
/// Pure function: <c>(measurement, rule) → verdict</c>. Stateless and deterministic, mirrored in
/// TypeScript for the offline wizard's live indicators; behaviour is pinned by the shared fixtures
/// in <c>tests/fixtures/passfail</c>.
/// </summary>
public static class PassFailCalculator
{
    public static PassFailVerdict Evaluate(double? value, PassFailRule rule)
    {
        if (string.Equals(rule.Kind, "none", StringComparison.OrdinalIgnoreCase))
            return PassFailVerdict.NoStandard;
        if (value is not { } v || double.IsNaN(v))
            return PassFailVerdict.NoStandard;

        return rule.Kind switch
        {
            "atMost" => rule.Limit is { } l ? Verdict(v <= l) : PassFailVerdict.NoStandard,
            "atLeast" => rule.Min is { } m ? Verdict(v >= m) : PassFailVerdict.NoStandard,
            "between" => rule is { Min: { } mn, Max: { } mx } ? Verdict(v >= mn && v <= mx) : PassFailVerdict.NoStandard,
            "tolerance" => rule is { Target: { } t, Tolerance: { } tol } ? Verdict(Math.Abs(v - t) <= tol) : PassFailVerdict.NoStandard,
            _ => PassFailVerdict.NoStandard,
        };
    }

    private static PassFailVerdict Verdict(bool pass) => pass ? PassFailVerdict.Pass : PassFailVerdict.Fail;
}
