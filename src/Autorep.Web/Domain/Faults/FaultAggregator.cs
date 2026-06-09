namespace Autorep.Web.Domain.Faults;

public sealed record FaultInput(
    string Component, string Description, string Severity, string Source, string? Recommendation = null);

public sealed record FaultGroup(string Component, string Severity, IReadOnlyList<FaultInput> Faults);

public sealed record FaultSummary(IReadOnlyList<FaultGroup> Groups, int Critical, int Major, int Minor, int Total);

/// <summary>
/// Pure function: the test's faults (Visual Faults + failed numerical readings) → a FaultSummary
/// grouped by component, each group rated by its worst fault, ordered worst-severity-first. Mirrored
/// in TypeScript for the offline wizard; pinned by the shared fixtures in <c>tests/fixtures/faults</c>.
/// </summary>
public static class FaultAggregator
{
    private static int Rank(string severity) => severity switch
    {
        "Critical" => 3,
        "Major" => 2,
        _ => 1,
    };

    public static FaultSummary Aggregate(IEnumerable<FaultInput> faults)
    {
        var all = faults.ToList();

        var groups = all
            .GroupBy(f => f.Component)
            .Select(g => new FaultGroup(
                g.Key,
                g.Select(f => f.Severity).OrderByDescending(Rank).First(),
                g.ToList()))
            .OrderByDescending(g => Rank(g.Severity))
            .ThenBy(g => g.Component, StringComparer.Ordinal)
            .ToList();

        int Count(string s) => all.Count(f => f.Severity == s);
        return new FaultSummary(groups, Count("Critical"), Count("Major"), Count("Minor"), all.Count);
    }
}
