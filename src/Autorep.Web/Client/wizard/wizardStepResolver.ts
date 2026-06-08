// TypeScript mirror of Domain/Wizard/WizardStepResolver.cs. Pure function:
// MachineConfiguration -> ordered WizardPlan. Kept byte-for-byte equivalent to the .NET
// resolver and pinned by the shared fixtures in tests/fixtures/wizard (see the .test.ts).
import type {
  MachineConfiguration,
  ResolvedWizardStep,
  WizardPlan,
  WizardStep,
} from "./types";

function step(
  stepKey: WizardStep,
  title: string,
  isOptional = false,
  sections: string[] = [],
): ResolvedWizardStep {
  return { step: stepKey, title, isOptional, sections };
}

const isRotary = (c: MachineConfiguration): boolean => c.plantType === "Rotary";

// Visual Faults — Running: rotary rotation vs herringbone bail area, plus the common sub-sections.
function runningSections(c: MachineConfiguration): string[] {
  return [isRotary(c) ? "Rotaries" : "BailArea", "MainAirline", "Inlets", "Clusters"];
}

// Test Record: ISO groups 1–9. Minimum-pump-speed vacuum only when a VSD is fitted.
function testRecordSections(c: MachineConfiguration): string[] {
  const s = ["SystemVacuumLevels"];
  if (c.vsdFitted) s.push("MinPumpSpeedVacuum");
  s.push(
    "ReserveCharacteristics",
    "RegulationCharacteristics",
    "VacuumDropAirline",
    "RegulatorSensitivity",
    "ReserveVacuumOffCluster",
    "VacuumGaugeAccuracy",
    "VacuumPumpTest",
    "PumpExhaustPressure",
  );
  return s;
}

// Additional Tests: ISO 10–12 plus per-ancillary consumption/leakage sub-sections.
function additionalSections(c: MachineConfiguration): string[] {
  const s = ["AirlineMilkSystemLeakage"];
  if (c.hasAcr) s.push("AcrConsumption");
  s.push("ClusterAirAdmission");
  if (c.hasMilkMeters) s.push("MilkMeter");
  if (c.hasTeatSprayer) s.push("TeatSpray");
  if (c.hasBailGates || c.hasBackingGate) s.push("GateCylinder");
  if (c.hasReleaserPump) s.push("ReleaserPumpHeads");
  s.push("RegulatorLoad");
  return s;
}

export function resolveWizard(config: MachineConfiguration): WizardPlan {
  const steps: ResolvedWizardStep[] = [
    step("Setup", "Farm & Your Details"),
    step("MachineConfiguration", "Machine Configuration & Ancillary"),
    step("VisualFaultsPreStart", "Visual Faults — Pre-Start"),
    step("VisualFaultsRunning", "Visual Faults — Running", false, runningSections(config)),
    step("TestRecord", "Test Record (Vacuum, Airflow & Pump)", false, testRecordSections(config)),
    step("AdditionalTests", "Additional Tests", false, additionalSections(config)),
    step("PulsatorTest", "Pulsator Test Results"),
    step("IndividualClusterTest", "Individual Cluster Tests", true),
    step("FaultSummary", "Fault Summary & Recommendations"),
    step("ReviewSignOff", "Review & Sign-Off"),
  ];

  return { steps, isShortTest: !config.isoPortsAvailable };
}
