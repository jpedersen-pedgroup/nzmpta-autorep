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
            // Order follows the NZMPTA ISO flowchart: vacuum (1–9) → airflow (10–12) → individual
            // cluster (13, optional; the wizard shows it only when 12b fails) → pulsation (14–15) →
            // the additional tests. Decided with Josh, 15 Sep 2026, after a tester skipped 10–12
            // because they sat under "Additional Tests".
            Step(WizardStep.TestRecord, "Vacuum Tests (ISO 1–9)", sections: TestRecordSections(config)),
            Step(WizardStep.AirflowTests, "Airflow Tests (ISO 10–12)", sections: AirflowSections(config)),
            Step(WizardStep.IndividualClusterTest, "Individual Cluster Tests (ISO 13)", optional: true),
            Step(WizardStep.PulsatorTest, "Pulsation & Ancillary (ISO 14–15)"),
            Step(WizardStep.AdditionalTests, "Additional Tests", sections: AdditionalSections(config)),
            Step(WizardStep.FaultSummary, "Fault Summary & Recommendations"),
            Step(WizardStep.ReviewSignOff, "Review & Sign-Off"),
        };

        return new WizardPlan(steps, IsShortTest: !config.IsoPortsAvailable);
    }

    private static ResolvedWizardStep Step(
        WizardStep step, string title, bool optional = false, IReadOnlyList<string>? sections = null)
        => new(step, title, optional, sections ?? Array.Empty<string>());

    // Visual Faults — Running: the full VisualFaultsMMRunning1–4 group set. Bail vs rotary swaps with
    // the plant type; ACR / milk-meter groups appear only when that equipment is fitted; the rest are
    // core to every machine (the Tester marks absent items blank / N/A).
    private static IReadOnlyList<string> RunningSections(MachineConfiguration c)
    {
        var s = new List<string>
        {
            c.IsRotary ? "Rotaries" : "BailArea",
            "MainAirline",
            "Inlets",
            "Clusters",
            "Claw",
            "Liner",
            "Shell",
            "ShortPulseTube",
            "LongPulseTube",
            "LongMilkTube",
            "Platform",
            "MilkFlowIndicator",
        };
        if (c.HasAcr) s.Add("Acr");
        if (c.HasMilkMeters) s.Add("MilkMeter");
        s.Add("Pulsation");
        s.Add("VacuumGauge");
        s.Add("Regulator");
        s.Add("Receiver");
        s.Add("VacuumPumpRunning");
        s.Add("Jetters");
        return s;
    }

    // Vacuum tests: ISO groups 1–9. Minimum-pump-speed vacuum only when a VSD is fitted. Pump
    // exhaust (9) lives inside the vacuum-pump section — on the flowchart it is an exception taken
    // only when the pump is out of spec, not a test of its own.
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
        return s;
    }

    // Airflow tests: ISO 10–12. ACR consumption only when ACRs are fitted.
    private static IReadOnlyList<string> AirflowSections(MachineConfiguration c)
    {
        var s = new List<string> { "AirlineMilkSystemLeakage" };
        if (c.HasAcr) s.Add("AcrConsumption");
        s.Add("ClusterAirAdmission");
        return s;
    }

    // Additional Tests: the unnumbered per-ancillary consumption/leakage checks that follow the
    // ISO flowchart — NZMPTA's "Additional Tests" flowchart, so this step must hold nothing else.
    private static IReadOnlyList<string> AdditionalSections(MachineConfiguration c)
    {
        var s = new List<string>();
        if (c.HasMilkMeters) s.Add("MilkMeter");
        if (c.HasTeatSprayer) s.Add("TeatSpray");
        if (c.HasBailGates || c.HasBackingGate) s.Add("GateCylinder");
        if (c.HasReleaserPump) s.Add("ReleaserPumpHeads");
        s.Add("RegulatorLoad");
        return s;
    }
}
