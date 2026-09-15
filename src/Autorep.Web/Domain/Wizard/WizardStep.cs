namespace Autorep.Web.Domain.Wizard;

/// <summary>
/// The top-level steps of the Tester wizard. Declaration order is NOT the wizard order — the
/// resolver owns that (it follows the NZMPTA ISO flowchart: vacuum 1–9, airflow 10–12, individual
/// cluster 13, pulsation 14–15, then the unnumbered additional tests). Names are the contract:
/// a test's current step is persisted by name on devices and in the payload, so never rename one.
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
    /// <summary>ISO 10–12 (leakage, ACR, cluster air admission). Added 15 Sep 2026 — these sat
    /// under "Additional Tests", which to a tester means the separate post-15 flowchart.</summary>
    AirflowTests,
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
