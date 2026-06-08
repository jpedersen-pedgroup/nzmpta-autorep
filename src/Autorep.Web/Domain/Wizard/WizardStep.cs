namespace Autorep.Web.Domain.Wizard;

/// <summary>
/// The top-level steps of the Tester wizard, in canonical order. Mirrors the legacy AutoRep Plus
/// form flow (see <c>plans/reference/test-workflow-and-faults.md</c> §D) with the PRD's
/// Visual-Faults pre-start/running split and a final sign-off.
/// </summary>
public enum WizardStep
{
    Setup,
    MachineConfiguration,
    VisualFaultsPreStart,
    VisualFaultsRunning,
    TestRecord,
    AdditionalTests,
    PulsatorTest,
    IndividualClusterTest,
    FaultSummary,
    ReviewSignOff,
}

/// <summary>A wizard step that applies to a given Machine Configuration.</summary>
/// <param name="Step">Which step.</param>
/// <param name="Title">Display title.</param>
/// <param name="IsOptional">The Tester may skip this step (e.g. Individual Cluster Tests).</param>
/// <param name="Sections">Ordered keys of the sub-sections visible within this step for this
/// configuration (empty when the step has no configuration-driven sub-sections).</param>
public sealed record ResolvedWizardStep(
    WizardStep Step,
    string Title,
    bool IsOptional,
    IReadOnlyList<string> Sections);

/// <summary>The ordered set of steps for a Machine Configuration, plus test-wide flags.</summary>
/// <param name="Steps">Visible steps, in order.</param>
/// <param name="IsShortTest">ISO ports unavailable — the reduced "short test" applies.</param>
public sealed record WizardPlan(IReadOnlyList<ResolvedWizardStep> Steps, bool IsShortTest);
