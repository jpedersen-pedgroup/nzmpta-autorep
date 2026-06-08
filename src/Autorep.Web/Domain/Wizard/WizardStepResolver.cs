using Autorep.Web.Domain.Entities;

namespace Autorep.Web.Domain.Wizard;

/// <summary>
/// Pure function: a <see cref="MachineConfiguration"/> → the ordered <see cref="WizardPlan"/>
/// (which steps and sub-sections apply). Stateless and deterministic so it can be unit-tested
/// table-driven and mirrored in TypeScript for the offline PWA. Behaviour is pinned by the
/// shared fixtures in <c>tests/fixtures/wizard</c>.
/// </summary>
public static class WizardStepResolver
{
    public static WizardPlan Resolve(MachineConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var steps = new List<ResolvedWizardStep>
        {
            Step(WizardStep.Setup, "Farm & Your Details"),
            Step(WizardStep.MachineConfiguration, "Machine Configuration & Ancillary"),
            Step(WizardStep.VisualFaultsPreStart, "Visual Faults — Pre-Start"),
            Step(WizardStep.VisualFaultsRunning, "Visual Faults — Running", sections: RunningSections(config)),
            Step(WizardStep.TestRecord, "Test Record (Vacuum, Airflow & Pump)", sections: TestRecordSections(config)),
            Step(WizardStep.AdditionalTests, "Additional Tests", sections: AdditionalSections(config)),
            Step(WizardStep.PulsatorTest, "Pulsator Test Results"),
            Step(WizardStep.IndividualClusterTest, "Individual Cluster Tests", optional: true),
            Step(WizardStep.FaultSummary, "Fault Summary & Recommendations"),
            Step(WizardStep.ReviewSignOff, "Review & Sign-Off"),
        };

        return new WizardPlan(steps, IsShortTest: !config.IsoPortsAvailable);
    }

    private static ResolvedWizardStep Step(
        WizardStep step, string title, bool optional = false, IReadOnlyList<string>? sections = null)
        => new(step, title, optional, sections ?? Array.Empty<string>());

    // Visual Faults — Running: rotary rotation vs herringbone bail area, plus the common sub-sections.
    private static IReadOnlyList<string> RunningSections(MachineConfiguration c) => new List<string>
    {
        c.IsRotary ? "Rotaries" : "BailArea",
        "MainAirline",
        "Inlets",
        "Clusters",
    };

    // Test Record: ISO groups 1–9. Minimum-pump-speed vacuum only when a VSD is fitted.
    private static IReadOnlyList<string> TestRecordSections(MachineConfiguration c)
    {
        var s = new List<string> { "SystemVacuumLevels" };
        if (c.VsdFitted) s.Add("MinPumpSpeedVacuum");
        s.Add("ReserveCharacteristics");
        s.Add("RegulationCharacteristics");
        s.Add("VacuumDropAirline");
        s.Add("RegulatorSensitivity");
        s.Add("ReserveVacuumOffCluster");
        s.Add("VacuumGaugeAccuracy");
        s.Add("VacuumPumpTest");
        s.Add("PumpExhaustPressure");
        return s;
    }

    // Additional Tests: ISO 10–12 plus per-ancillary consumption/leakage sub-sections.
    private static IReadOnlyList<string> AdditionalSections(MachineConfiguration c)
    {
        var s = new List<string> { "AirlineMilkSystemLeakage" };
        if (c.HasAcr) s.Add("AcrConsumption");
        s.Add("ClusterAirAdmission");
        if (c.HasMilkMeters) s.Add("MilkMeter");
        if (c.HasTeatSprayer) s.Add("TeatSpray");
        if (c.HasBailGates || c.HasBackingGate) s.Add("GateCylinder");
        if (c.HasReleaserPump) s.Add("ReleaserPumpHeads");
        s.Add("RegulatorLoad");
        return s;
    }
}
