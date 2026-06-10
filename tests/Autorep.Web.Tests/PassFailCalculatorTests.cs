using System.Text.Json;
using Autorep.Web.Domain.PassFail;
using FluentAssertions;

namespace Autorep.Web.Tests;

// Pins the Pass/Fail Calculator against the shared fixtures (tests/fixtures/passfail) — the same
// JSON the TypeScript Vitest suite uses, so the two implementations can't drift.
public class PassFailCalculatorTests
{
    private sealed record RuleCase(double? Value, string Expected);
    private sealed record RuleFixture(string Name, PassFailRule Rule, RuleCase[] Cases);

    [Fact]
    public void Evaluate_matches_all_shared_fixtures()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "PassFail", "cases.json");
        var fixtures = JsonSerializer.Deserialize<RuleFixture[]>(
            File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        fixtures.Should().NotBeEmpty();

        foreach (var f in fixtures)
        {
            foreach (var c in f.Cases)
            {
                var verdict = PassFailCalculator.Evaluate(c.Value, f.Rule) switch
                {
                    PassFailVerdict.Pass => "pass",
                    PassFailVerdict.Fail => "fail",
                    _ => "noStandard",
                };
                verdict.Should().Be(c.Expected, "{0} @ value={1}", f.Name, c.Value);
            }
        }
    }
}
