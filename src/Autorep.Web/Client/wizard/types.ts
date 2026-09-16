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
  | "AirflowTests"
  | "AdditionalTests"
  | "PulsatorTest"
  | "IndividualClusterTest"
  | "FaultSummary"
  | "ReviewSignOff";

/** One vacuum pump's details (the legacy "Farm & Milking Machine" page-2 row). Positional: pump N
 * lines up with the pump-N readings (8a-8c). Read through wizard/pumpRows.ts, never directly. */
export type VacuumPumpDetail = {
  make?: string | null;
  model?: string | null;
  /** Motor size as it reads on the plate - free text, usually kW. */
  motorSize?: string | null;
  drivesMilkPump?: boolean;
  regulatorType?: string | null;
};

/** One releaser (milk) pump's details. */
export type ReleaserPumpDetail = {
  make?: string | null;
  model?: string | null;
  motorSize?: string | null;
};

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
  /** Make/model/motor/regulator per vacuum pump, in pump order. Absent on tests captured before
   * 16 Sep 2026, and rows past the pump count are kept but not shown - see wizard/pumpRows.ts. */
  vacuumPumps?: VacuumPumpDetail[];
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
  /** Releaser (milk) pump details, listed while hasReleaserPump - see wizard/pumpRows.ts. */
  releaserPumps?: ReleaserPumpDetail[];
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
  /** The specific standard fault chosen from the check's Lookup list (when faulted). */
  observation?: string;
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
    vacuumPumps: [],
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
    releaserPumps: [],
  };
}
