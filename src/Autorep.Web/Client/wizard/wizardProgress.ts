// Derived view-model shared by every wizard shell (rail / single-scroll / task hub). Pure
// functions over a LocalTest — no Preact, no IndexedDB — so the layouts stay presentation-only and
// this stays unit-testable.
//
// The rail only ever needed "is this step done?", but the scroll and hub layouts show how FAR
// through each step you are and how many faults it holds, so completeness gets a fraction here as
// well as the boolean. Both must agree: a step reading 100% must also be in computeCompleted().
import { resolveWizard } from "./wizardStepResolver";
import type { MachineConfiguration, ResolvedWizardStep, WizardStep } from "./types";
import type { LocalTest } from "../db/testStore";
import {
  checklistComplete,
  preStartSections,
  runningSectionsFor,
  type ChecklistSection,
} from "./visualChecklist";
import {
  additionalTestSections,
  pulsatorSections,
  testRecordSections,
  type ReadingSection,
} from "../passfail/standards";
import { evaluate } from "../passfail/passFail";
import { buildFaultInputs } from "../faults/buildFaults";
import { aggregate, type FaultSummary } from "../faults/faultAggregator";

/** The resolver's Visual Faults — Running section keys for this machine. The resolver owns which
 * sections apply (bail vs rotary, ACR / milk meters), so ask it rather than re-deriving. */
export function runningSectionKeys(config: MachineConfiguration): string[] {
  return resolveWizard(config).steps.find((s) => s.step === "VisualFaultsRunning")?.sections ?? [];
}

function runningChecklist(config: MachineConfiguration): ChecklistSection[] {
  return runningSectionsFor(runningSectionKeys(config));
}

/** Checklist items that gate completeness — data-capture fields (sizes, lengths) are optional and
 * excluded, matching checklistComplete(). */
function gatingItems(sections: ChecklistSection[]) {
  return sections.flatMap((s) => s.items.filter((it) => !it.data));
}

function readingKeys(sections: ReadingSection[]): string[] {
  return sections.flatMap((s) => s.readings.map((r) => r.key));
}

/** Which steps are finished. Moved here from WizardApp so every shell shares one definition. */
export function computeCompleted(t: LocalTest): Set<WizardStep> {
  const done = new Set<WizardStep>();
  if (t.farmName.trim().length > 0) done.add("Setup");
  if (t.config.clusterCount > 0) done.add("MachineConfiguration");
  if (checklistComplete(preStartSections(t.config.hasReleaserPump), t.visualFaults)) {
    done.add("VisualFaultsPreStart");
  }
  if (checklistComplete(runningChecklist(t.config), t.visualFaults)) {
    done.add("VisualFaultsRunning");
  }
  if (testRecordSections(t.config, t.readings).every((s) => s.readings.every((r) => t.readings[r.key] != null))) {
    done.add("TestRecord");
  }
  if (additionalTestSections(t.config, t.readings).every((s) => s.readings.every((r) => t.readings[r.key] != null))) {
    done.add("AdditionalTests");
  }
  if ((t.pulsatorRows ?? []).length > 0) done.add("PulsatorTest");
  if ((t.clusterRows ?? []).length > 0) done.add("IndividualClusterTest");
  const faults = buildFaultInputs(t);
  if (faults.every((f) => f.key != null && (t.recommendations[f.key] ?? "").trim().length > 0)) {
    done.add("FaultSummary");
  }
  return done;
}

/** How far through a step the Tester is, 0..1. Steps with nothing to count are all-or-nothing.
 * Kept consistent with computeCompleted(): anything it calls complete returns 1 here. */
export function stepProgress(t: LocalTest, step: WizardStep): number {
  const cfg = t.config;
  switch (step) {
    case "Setup":
      return t.farmName.trim().length > 0 ? 1 : 0;
    case "MachineConfiguration":
      return cfg.clusterCount > 0 ? 1 : 0;
    case "VisualFaultsPreStart":
    case "VisualFaultsRunning": {
      const sections =
        step === "VisualFaultsPreStart" ? preStartSections(cfg.hasReleaserPump) : runningChecklist(cfg);
      const items = gatingItems(sections);
      if (items.length === 0) return 0;
      return items.filter((it) => t.visualFaults[it.key]?.status !== undefined).length / items.length;
    }
    case "TestRecord":
    case "AdditionalTests": {
      const sections =
        step === "TestRecord"
          ? testRecordSections(cfg, t.readings)
          : additionalTestSections(cfg, t.readings);
      const keys = readingKeys(sections);
      if (keys.length === 0) return 0;
      return keys.filter((k) => t.readings[k] != null).length / keys.length;
    }
    case "PulsatorTest":
      return (t.pulsatorRows ?? []).length > 0 ? 1 : 0;
    case "IndividualClusterTest":
      return (t.clusterRows ?? []).length > 0 ? 1 : 0;
    case "FaultSummary": {
      const faults = buildFaultInputs(t);
      // No faults means nothing to write up — done, not stalled at zero. computeCompleted()
      // agrees (every() over an empty list is true), and the two must not disagree.
      if (faults.length === 0) return 1;
      // Same guard as computeCompleted — a fault with no key can't carry a recommendation.
      const written = faults.filter((f) => f.key != null && (t.recommendations[f.key] ?? "").trim().length > 0);
      return written.length / faults.length;
    }
    case "ReviewSignOff":
      return t.markedCompleteAt ? 1 : 0;
  }
}

/** Faults logged against a step — checklist items marked Fault, plus readings failing a standard.
 * Steps that can't hold a fault return 0. */
export function faultsInStep(t: LocalTest, step: WizardStep): number {
  const cfg = t.config;
  switch (step) {
    case "VisualFaultsPreStart":
    case "VisualFaultsRunning": {
      const sections =
        step === "VisualFaultsPreStart" ? preStartSections(cfg.hasReleaserPump) : runningChecklist(cfg);
      return sections
        .flatMap((s) => s.items)
        .filter((it) => t.visualFaults[it.key]?.status === "fault").length;
    }
    case "TestRecord":
    case "AdditionalTests": {
      const sections =
        step === "TestRecord"
          ? testRecordSections(cfg, t.readings)
          : additionalTestSections(cfg, t.readings);
      return sections
        .flatMap((s) => s.readings)
        .filter((r) => evaluate(t.readings[r.key], r.rule) === "fail").length;
    }
    default:
      return 0;
  }
}

export interface OverallProgress {
  /** 0–100, over the required steps only. */
  pct: number;
  doneCount: number;
  requiredCount: number;
  /** The step to resume at — first incomplete, or null when only sign-off is left. Comes from a
   * plan resolved here, so it is equal to but not the same object as the caller's plan entry:
   * compare on `.step`, never by identity or indexOf. */
  firstIncomplete: ResolvedWizardStep | null;
}

/** Headline progress. Optional steps and Review & Sign-Off are excluded from the denominator —
 * an optional step left undone shouldn't hold the bar below 100%. */
export function overallProgress(t: LocalTest): OverallProgress {
  const plan = resolveWizard(t.config);
  const completed = computeCompleted(t);
  const required = plan.steps.filter((s) => !s.isOptional && s.step !== "ReviewSignOff");
  const doneCount = required.filter((s) => completed.has(s.step)).length;
  return {
    pct: required.length === 0 ? 0 : Math.round((doneCount / required.length) * 100),
    doneCount,
    requiredCount: required.length,
    firstIncomplete: plan.steps.find((s) => !completed.has(s.step) && s.step !== "ReviewSignOff") ?? null,
  };
}

/** Every fault on the test, grouped and counted by severity. */
export function faultSummary(t: LocalTest): FaultSummary {
  return aggregate(buildFaultInputs(t));
}

export interface SubSection {
  key: string;
  title: string;
}

/** The pages within a step, for layouts that paginate inside one (the hub's focus dots). Steps
 * without meaningful sub-pages return a single entry — including Machine Configuration, which
 * carries its own tab strip and shouldn't be split twice. */
export function subsFor(t: LocalTest, step: WizardStep): SubSection[] {
  const cfg = t.config;
  const single = (title: string): SubSection[] => [{ key: "single", title }];
  switch (step) {
    case "VisualFaultsPreStart":
      return preStartSections(cfg.hasReleaserPump);
    case "VisualFaultsRunning":
      return runningChecklist(cfg);
    case "TestRecord":
      return testRecordSections(cfg, t.readings);
    case "AdditionalTests":
      return additionalTestSections(cfg, t.readings);
    case "PulsatorTest":
      return [{ key: "rows", title: "Per-pulsator results" }, ...pulsatorSections(cfg, t.readings)];
    case "IndividualClusterTest":
      return [{ key: "rows", title: "Per-cluster results" }];
    default:
      return single("");
  }
}

/** One-line "what this step is for", shown under the title on hub cards and scroll blocks. */
export const STEP_DESCRIPTIONS: Record<WizardStep, string> = {
  Setup: "Farm record, contacts, calibration dates",
  MachineConfiguration: "Declare what's fitted — later steps adapt",
  VisualFaultsPreStart: "Machine off · vacuum pumps, releaser",
  VisualFaultsRunning: "Machine running · airline to jetters",
  TestRecord: "ISO 1–9 · vacuum, reserve, gauges, pump",
  AdditionalTests: "ISO 10–12 · leakage + this machine's ancillaries",
  PulsatorTest: "Rates, ratios, phases + spread checks",
  IndividualClusterTest: "ISO 13 · air admission, leakage, vent",
  FaultSummary: "Add a recommendation for every fault",
  ReviewSignOff: "Attest, complete, and generate the report",
};

/** Short labels for the scroll layout's chip strip, where the full titles don't fit. */
export const STEP_SHORT_LABELS: Record<WizardStep, string> = {
  Setup: "Farm",
  MachineConfiguration: "Machine",
  VisualFaultsPreStart: "Pre-start",
  VisualFaultsRunning: "Running",
  TestRecord: "Test record",
  AdditionalTests: "Additional",
  PulsatorTest: "Pulsators",
  IndividualClusterTest: "Clusters",
  FaultSummary: "Faults",
  ReviewSignOff: "Sign-off",
};
