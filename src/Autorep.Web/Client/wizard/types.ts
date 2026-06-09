// TypeScript mirror of the .NET wizard domain types
// (src/Autorep.Web/Domain/Entities/MachineConfiguration.cs + Domain/Wizard/WizardStep.cs).
// String unions use the .NET enum/step *names* so the shared JSON fixtures in
// tests/fixtures/wizard deserialize identically on both sides.

export type PlantType =
  | "HerringboneLowline"
  | "HerringboneHighline"
  | "Rotary"
  | "Other";

export type PumpLubrication = "OilLubricated" | "LiquidRing" | "Other";

export type WizardStep =
  | "Setup"
  | "MachineConfiguration"
  | "VisualFaultsPreStart"
  | "VisualFaultsRunning"
  | "TestRecord"
  | "AdditionalTests"
  | "PulsatorTest"
  | "IndividualClusterTest"
  | "FaultSummary"
  | "ReviewSignOff";

export interface MachineConfiguration {
  plantType: PlantType;
  /** Free-text plant size descriptor (legacy PlantSize, e.g. "30 a-side"). */
  plantSize?: string | null;
  clusterCount: number;
  herdSize?: number | null;
  lastBmcc?: string | null;
  milklineSize?: string | null;
  /** Atmospheric pressure at sea level (kPa) — selects the airflow correction factor. */
  atmosPressureSeaLevel?: number | null;
  flushingPulsationSystem: boolean;
  pulsatorBrand?: string | null;
  pulsatorModel?: string | null;
  /** Pulsator configuration (legacy PulsatorSize, e.g. "2 X 2", "4 + 0"). */
  pulsatorConfiguration?: string | null;
  pulsatorCount: number;
  clawModel?: string | null;
  shellModel?: string | null;
  /** Front liner (legacy Liner). */
  linerModel?: string | null;
  /** Back liner (legacy BackLiner). */
  backLiner?: string | null;
  linerVented: boolean;
  numberOfVacuumPumps: number;
  pumpLubrication: PumpLubrication;
  vsdFitted: boolean;
  isoPortsAvailable: boolean;
  hasPulsatorStopSystem: boolean;
  hasAcr: boolean;
  hasBailGates: boolean;
  hasMilkMeters: boolean;
  hasTeatSprayer: boolean;
  hasBackingGate: boolean;
  hasReleaserPump: boolean;
}

export interface ResolvedWizardStep {
  step: WizardStep;
  title: string;
  isOptional: boolean;
  sections: string[];
}

export interface WizardPlan {
  steps: ResolvedWizardStep[];
  isShortTest: boolean;
}

export type FaultSeverity = "Critical" | "Major" | "Minor";

/** A visual-checklist item outcome: OK or a logged fault. Blank items are simply absent. */
export interface VisualFaultEntry {
  status: "ok" | "fault";
  severity?: FaultSeverity;
  note?: string;
}

/** Records a use of "Check all as verified" on a wizard step (the PRD attestation trail). */
export interface ChecklistAttestation {
  step: WizardStep;
  /** The checklist section attested ("Check all as verified" is per-section/tab). */
  section?: string;
  attestedAt: string;
  text: string;
}

/** Mirrors the .NET entity defaults so partial fixtures resolve identically. */
export function defaultMachineConfiguration(): MachineConfiguration {
  return {
    plantType: "HerringboneLowline",
    plantSize: null,
    clusterCount: 0,
    herdSize: null,
    lastBmcc: null,
    milklineSize: null,
    atmosPressureSeaLevel: null,
    flushingPulsationSystem: false,
    pulsatorBrand: null,
    pulsatorModel: null,
    pulsatorConfiguration: null,
    pulsatorCount: 0,
    clawModel: null,
    shellModel: null,
    linerModel: null,
    backLiner: null,
    linerVented: false,
    numberOfVacuumPumps: 1,
    pumpLubrication: "OilLubricated",
    vsdFitted: false,
    isoPortsAvailable: true,
    hasPulsatorStopSystem: false,
    hasAcr: false,
    hasBailGates: false,
    hasMilkMeters: false,
    hasTeatSprayer: false,
    hasBackingGate: false,
    hasReleaserPump: false,
  };
}
