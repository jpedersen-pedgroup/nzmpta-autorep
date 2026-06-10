using System.Text.Json;
using Autorep.Web.Domain.Faults;
using FluentAssertions;

namespace Autorep.Web.Tests;

// Pins the Fault Aggregator against the shared fixtures (tests/fixtures/faults) — the same JSON the
// TypeScript Vitest suite uses, so grouping / severity rollup / ordering stay in lockstep.
public class FaultAggregatorTests
{
    private sealed record ExpectedGroup(string Component, string Severity, int Count);
    private sealed record Fixture(
        string Name, FaultInput[] Inputs, ExpectedGroup[] ExpectedGroups,
        int Critical, int Major, int Minor, int Total);

    [Fact]
    public void Aggregate_matches_all_shared_fixtures()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Faults", "cases.json");
        var fixtures = JsonSerializer.Deserialize<Fixture[]>(
            File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        fixtures.Should().NotBeEmpty();

        foreach (var fx in fixtures)
        {
            var summary = FaultAggregator.Aggregate(fx.Inputs);

            summary.Groups.Select(g => new ExpectedGroup(g.Component, g.Severity, g.Faults.Count))
                .Should().Equal(fx.ExpectedGroups, "groups for '{0}'", fx.Name);
            summary.Critical.Should().Be(fx.Critical, fx.Name);
            summary.Major.Should().Be(fx.Major, fx.Name);
            summary.Minor.Should().Be(fx.Minor, fx.Name);
            summary.Total.Should().Be(fx.Total, fx.Name);
        }
    }
}
