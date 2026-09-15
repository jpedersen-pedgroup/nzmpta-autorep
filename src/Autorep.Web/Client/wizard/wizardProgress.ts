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
  airflowSections,
  pulsatorSections,
  testRecordSections,
  type ReadingSection,
} from "../passfail/standards";
import { evaluate } from "../passfail/passFail";
import { recordedRows } from "../ui/measurementRows";
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

/** Checklist items that gate completeness — data-capture fields (sizes, lengths) and choices
 * (tube type, cluster position) are optional and excluded, matching checklistComplete(). */
function gatingItems(sections: ChecklistSection[]) {
  return sections.flatMap((s) => s.items.filter((it) => !it.data && !it.choice));
}

/** The readings the tester has to ENTER. Calculated rows (passfail/derived.ts) fill themselves once
 * their inputs exist, so they never gate a step — a calculated row that can't be worked out on this
 * machine (a cleaning reserve with no milkline size) must not hold Test Record at 95% for ever. */
function readingKeys(sections: ReadingSection[]): string[] {
  return sections.flatMap((s) => s.readings.filter((r) => !r.derived).map((r) => r.key));
}

type ReadingsStep = "TestRecord" | "AirflowTests" | "AdditionalTests" | "PulsatorTest";
const READINGS_STEPS: ReadingsStep[] = ["TestRecord", "AirflowTests", "AdditionalTests", "PulsatorTest"];

/** The reading sections a readings-driven step shows, so completeness, progress and fault counts
 * all work from the same list. Faulty-pulsator rows are deliberately not part of the pulsation
 * step's completeness: a clean machine has none to list. */
function readingSectionsFor(t: LocalTest, step: ReadingsStep): ReadingSection[] {
  switch (step) {
    case "TestRecord":
      return testRecordSections(t.config, t.readings);
    case "AirflowTests":
      return airflowSections(t.config, t.readings);
    case "AdditionalTests":
      return additionalTestSections(t.config, t.readings);
    case "PulsatorTest":
      return pulsatorSections(t.config, t.readings);
  }
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
  for (const step of READINGS_STEPS) {
    if (readingKeys(readingSectionsFor(t, step)).every((k) => t.readings[k] != null)) done.add(step);
  }
  if (recordedRows(t.clusterRows).length > 0) done.add("IndividualClusterTest");
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
    case "AirflowTests":
    case "AdditionalTests":
    case "PulsatorTest": {
      const keys = readingKeys(readingSectionsFor(t, step));
      if (keys.length === 0) return 0;
      return keys.filter((k) => t.readings[k] != null).length / keys.length;
    }
    case "IndividualClusterTest":
      return recordedRows(t.clusterRows).length > 0 ? 1 : 0;
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
    case "AirflowTests":
    case "AdditionalTests":
    case "PulsatorTest":
      return readingSectionsFor(t, step)
        .flatMap((s) => s.readings)
        .filter((r) => evaluate(t.readings[r.key], r.rule) === "fail").length;
    default:
      return 0;
  }
}

export interface OverallProgress {
  /** 0–100, over the required steps only. */
  pct: number;
  doneCount: number;
  requiredCount: number;
  /** The first incomplete REQUIRED step — what the shells offer as "resume"/"next up" — or null
   * when only sign-off is left. Optional steps are skipped: a tester reaches those by choosing
   * them, and Individual Cluster Tests sits ahead of Fault Summary in the plan, so including it
   * would point at an untouched optional step while a required one behind it went unfinished.
   *
   * Comes from a plan resolved here, so it is equal to but not the same object as the caller's
   * plan entry: compare on `.step`, never by identity or indexOf. */
  firstIncomplete: ResolvedWizardStep | null;
}

/** Individual Cluster Tests (ISO 13) is only done when the machine's cluster air admission (12b)
 * failed — the flowchart's branch. It also stays once rows are recorded, so data is never hidden. */
export function clusterStepApplies(t: LocalTest): boolean {
  if (recordedRows(t.clusterRows).length > 0) return true;
  const def = airflowSections(t.config, t.readings)
    .flatMap((s) => s.readings)
    .find((r) => r.key === "add.clusterAirAdmission");
  return def != null && evaluate(t.readings[def.key], def.rule) === "fail";
}

/** The steps this test shows: the resolver's configuration-driven plan, minus the steps that
 * depend on results. The two resolvers stay identical and config-only; this is the client's
 * overlay, and every consumer of the plan must read it from here. */
export function visibleSteps(t: LocalTest): ResolvedWizardStep[] {
  const steps = resolveWizard(t.config).steps;
  return clusterStepApplies(t) ? steps : steps.filter((s) => s.step !== "IndividualClusterTest");
}

/** Where to land: the persisted step if it is still shown, else the nearest earlier step that is.
 * A test parked on Individual Cluster Tests whose 12b later passed, or one saved by an older build
 * on a step this one doesn't know, must not open on nothing. */
export function currentStepFor(t: LocalTest, visible: ResolvedWizardStep[]): WizardStep {
  if (visible.some((s) => s.step === t.currentStep)) return t.currentStep as WizardStep;
  const all = resolveWizard(t.config).steps.map((s) => s.step);
  const at = all.indexOf(t.currentStep as WizardStep);
  for (let i = at - 1; i >= 0; i--) {
    if (visible.some((s) => s.step === all[i])) return all[i];
  }
  return visible[0].step;
}

/** Headline progress. Optional steps and Review & Sign-Off are excluded — an optional step left
 * undone shouldn't hold the bar below 100%, nor claim to be what the test needs next. Both the
 * percentage and the resume target read from the same `required` list so they can't disagree. */
export function overallProgress(t: LocalTest): OverallProgress {
  const completed = computeCompleted(t);
  const required = visibleSteps(t).filter((s) => !s.isOptional && s.step !== "ReviewSignOff");
  const doneCount = required.filter((s) => completed.has(s.step)).length;
  return {
    pct: required.length === 0 ? 0 : Math.round((doneCount / required.length) * 100),
    doneCount,
    requiredCount: required.length,
    firstIncomplete: required.find((s) => !completed.has(s.step)) ?? null,
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
    case "AirflowTests":
      return airflowSections(cfg, t.readings);
    case "AdditionalTests":
      return additionalTestSections(cfg, t.readings);
    case "PulsatorTest":
      // Same order as the step renders: the ISO 14–15 readings, then the faulty-pulsator table.
      return [...pulsatorSections(cfg, t.readings), { key: "rows", title: "Faulty pulsators" }];
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
  AirflowTests: "ISO 10–12 · leakage, ACRs, cluster air admission",
  AdditionalTests: "This machine's ancillaries · meters, sprayer, gates, releaser",
  PulsatorTest: "ISO 14–15 · air consumption, test pulsation, faulty pulsators",
  IndividualClusterTest: "ISO 13 · only when cluster air admission fails",
  FaultSummary: "Add a recommendation for every fault",
  ReviewSignOff: "Attest, complete, and generate the report",
};

/** Short labels for the scroll layout's chip strip, where the full titles don't fit. */
export const STEP_SHORT_LABELS: Record<WizardStep, string> = {
  Setup: "Farm",
  MachineConfiguration: "Machine",
  VisualFaultsPreStart: "Pre-start",
  VisualFaultsRunning: "Running",
  TestRecord: "Vacuum",
  AirflowTests: "Airflow",
  AdditionalTests: "Additional",
  PulsatorTest: "Pulsation",
  IndividualClusterTest: "Clusters",
  FaultSummary: "Faults",
  ReviewSignOff: "Sign-off",
};
