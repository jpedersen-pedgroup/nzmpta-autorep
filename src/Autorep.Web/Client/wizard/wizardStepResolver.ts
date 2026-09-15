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

// Vacuum tests: ISO groups 1–9. Minimum-pump-speed vacuum only when a VSD is fitted. Pump exhaust
// (9) lives inside the vacuum-pump section — on the flowchart it is an exception taken only when
// the pump is out of spec, not a test of its own.
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
  );
  return s;
}

// Airflow tests: ISO 10–12. ACR consumption only when ACRs are fitted.
function airflowSections(c: MachineConfiguration): string[] {
  const s = ["AirlineMilkSystemLeakage"];
  if (c.hasAcr) s.push("AcrConsumption");
  s.push("ClusterAirAdmission");
  return s;
}

// Additional Tests: the unnumbered per-ancillary consumption/leakage checks that follow the ISO
// flowchart — NZMPTA's "Additional Tests" flowchart, so this step must hold nothing else.
function additionalSections(c: MachineConfiguration): string[] {
  const s: string[] = [];
  if (c.hasMilkMeters) s.push("MilkMeter");
  if (c.hasTeatSprayer) s.push("TeatSpray");
  if (c.hasBailGates || c.hasBackingGate) s.push("GateCylinder");
  if (c.hasReleaserPump) s.push("ReleaserPumpHeads");
  s.push("RegulatorLoad");
  return s;
}

// Step order follows the NZMPTA ISO flowchart: vacuum (1–9) → airflow (10–12) → individual
// cluster (13, optional; the wizard shows it only when 12b fails) → pulsation (14–15) → the
// additional tests. Decided with Josh, 15 Sep 2026, after a tester skipped 10–12 because they sat
// under "Additional Tests".
export function resolveWizard(config: MachineConfiguration): WizardPlan {
  const steps: ResolvedWizardStep[] = [
    step("Setup", "Farm & Your Details"),
    step("MachineConfiguration", "Machine Configuration & Ancillary"),
    step("VisualFaultsPreStart", "Visual Faults — Pre-Start"),
    step("VisualFaultsRunning", "Visual Faults — Running", false, runningSections(config)),
    step("TestRecord", "Vacuum Tests (ISO 1–9)", false, testRecordSections(config)),
    step("AirflowTests", "Airflow Tests (ISO 10–12)", false, airflowSections(config)),
    step("IndividualClusterTest", "Individual Cluster Tests (ISO 13)", true),
    step("PulsatorTest", "Pulsation & Ancillary (ISO 14–15)"),
    step("AdditionalTests", "Additional Tests", false, additionalSections(config)),
    step("FaultSummary", "Fault Summary & Recommendations"),
    step("ReviewSignOff", "Review & Sign-Off"),
  ];

  return { steps, isShortTest: !config.isoPortsAvailable };
}
