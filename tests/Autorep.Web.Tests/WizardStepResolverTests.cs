using System.Text.Json;
using System.Text.Json.Serialization;
using Autorep.Web.Domain.Entities;
using Autorep.Web.Domain.Wizard;
using FluentAssertions;

namespace Autorep.Web.Tests;

/// <summary>
/// Table-driven tests for the <see cref="WizardStepResolver"/>, pinned by the shared JSON fixtures
/// in <c>tests/fixtures/wizard</c> (the same fixtures will drive the TypeScript mirror).
/// </summary>
public class WizardStepResolverTests
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static IEnumerable<object[]> Fixtures()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Wizard");
        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
            yield return new object[] { Path.GetFileNameWithoutExtension(file), file };
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Resolve_matches_fixture(string name, string path)
    {
        var fx = JsonSerializer.Deserialize<Fixture>(File.ReadAllText(path), JsonOpts)!;

        var plan = WizardStepResolver.Resolve(fx.Config);

        plan.Steps.Select(s => s.Step.ToString())
            .Should().Equal(fx.ExpectedSteps, "step order for '{0}'", name);

        plan.Steps.Where(s => s.IsOptional).Select(s => s.Step.ToString())
            .Should().BeEquivalentTo(fx.OptionalSteps, "optional steps for '{0}'", name);

        plan.IsShortTest.Should().Be(fx.ShortTest, "short-test flag for '{0}'", name);

        foreach (var (stepName, expected) in fx.ExpectedSections)
        {
            var step = plan.Steps.Single(s => s.Step.ToString() == stepName);
            step.Sections.Should().Equal(expected, "sections of {0} for '{1}'", stepName, name);
        }
    }

    [Fact]
    public void Resolve_omits_min_pump_speed_section_without_vsd()
    {
        WizardStepResolver.Resolve(new MachineConfiguration { VsdFitted = false })
            .Steps.Single(s => s.Step == WizardStep.TestRecord).Sections
            .Should().NotContain("MinPumpSpeedVacuum");

        WizardStepResolver.Resolve(new MachineConfiguration { VsdFitted = true })
            .Steps.Single(s => s.Step == WizardStep.TestRecord).Sections
            .Should().Contain("MinPumpSpeedVacuum");
    }

    [Fact]
    public void Resolve_shows_acr_section_only_when_acr_present()
    {
        WizardStepResolver.Resolve(new MachineConfiguration { HasAcr = true })
            .Steps.Single(s => s.Step == WizardStep.AdditionalTests).Sections
            .Should().Contain("AcrConsumption");

        WizardStepResolver.Resolve(new MachineConfiguration { HasAcr = false })
            .Steps.Single(s => s.Step == WizardStep.AdditionalTests).Sections
            .Should().NotContain("AcrConsumption");
    }

    [Fact]
    public void Resolve_marks_individual_cluster_test_optional()
    {
        WizardStepResolver.Resolve(new MachineConfiguration())
            .Steps.Single(s => s.Step == WizardStep.IndividualClusterTest).IsOptional
            .Should().BeTrue();
    }

    private sealed record Fixture
    {
        public string Name { get; init; } = "";
        public MachineConfiguration Config { get; init; } = new();
        public string[] ExpectedSteps { get; init; } = Array.Empty<string>();
        public string[] OptionalSteps { get; init; } = Array.Empty<string>();
        public bool ShortTest { get; init; }
        public Dictionary<string, string[]> ExpectedSections { get; init; } = new();
    }
}
