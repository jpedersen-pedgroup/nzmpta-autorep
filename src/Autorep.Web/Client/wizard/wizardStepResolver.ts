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

// Visual Faults — Running: the full VisualFaultsMMRunning1–4 group set. Bail vs rotary swaps with
// the plant type; ACR / milk-meter groups appear only when that equipment is fitted; the rest are
// core to every machine (the Tester marks absent items blank / N/A).
function runningSections(c: MachineConfiguration): string[] {
  const s = [
    isRotary(c) ? "Rotaries" : "BailArea",
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
  ];
  if (c.hasAcr) s.push("Acr");
  if (c.hasMilkMeters) s.push("MilkMeter");
  s.push("Pulsation", "VacuumGauge", "Regulator", "Receiver", "VacuumPumpRunning", "Jetters");
  return s;
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
