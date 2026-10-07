// How each visual check of a version was verified (PRD story 69): item by item, or confirmed in bulk
// with "Check all as verified" — whose attestation names the section it covered (wizard/types
// ChecklistAttestation). Pure: worked out from the version's own checks and attestations, for the
// admin portal's audit panel.
import type { LocalTest } from "../db/testStore";
import type { ChecklistAttestation, WizardStep } from "../wizard/types";
import { isCheckItem, preStartSections, runningSectionsFor, type ChecklistSection } from "../wizard/visualChecklist";
import { runningSectionKeys } from "../wizard/wizardProgress";

/** "bulk": OK in a section confirmed with "Check all as verified" (applyCheckAll sets every check
 * that isn't a fault to OK, so the record can't tell whether any of them was also ticked by hand).
 * "individual": set one by one — every fault is, since checking all never records one. "unchecked":
 * left blank. */
export type ItemVerification = "bulk" | "individual" | "unchecked";

export interface ItemVerificationRow {
  key: string;
  label: string;
  status: "ok" | "fault" | null;
  verified: ItemVerification;
  /** A fault's severity and observation. */
  detail?: string;
}

export interface SectionVerification {
  step: WizardStep;
  key: string;
  title: string;
  /** The section's "Check all as verified" attestation, if it was used (the latest). */
  attestation: ChecklistAttestation | null;
  items: ItemVerificationRow[];
}

/** The steps whose checklists have "Check all as verified". The sign-off step's attestation is the
 * tester's declaration that the test is complete, not a bulk confirmation of any checks. */
const CHECKLIST_STEPS: readonly WizardStep[] = ["VisualFaultsPreStart", "VisualFaultsRunning"];

/** How many checklist sections were confirmed with "Check all as verified" (each counted once). */
export function bulkConfirmedSections(attestations: readonly ChecklistAttestation[]): number {
  return new Set(attestations.filter((a) => CHECKLIST_STEPS.includes(a.step)).map((a) => `${a.step}/${a.section ?? ""}`)).size;
}

/** Every visual-check section the version recorded anything in, with how each item was verified. */
export function checklistVerification(test: LocalTest): SectionVerification[] {
  const steps: Array<[WizardStep, ChecklistSection[]]> = [
    ["VisualFaultsPreStart", preStartSections(test.config.hasReleaserPump)],
    ["VisualFaultsRunning", runningSectionsFor(runningSectionKeys(test.config))],
  ];
  const latestFirst = [...(test.attestations ?? [])].reverse();
  const out: SectionVerification[] = [];
  for (const [step, sections] of steps) {
    for (const section of sections) {
      // The wizard always names the section; one that doesn't would have covered the whole step.
      const attestation =
        latestFirst.find((a) => a.step === step && (a.section ?? section.key) === section.key) ?? null;
      const items = section.items.filter(isCheckItem).map((item): ItemVerificationRow => {
        const entry = test.visualFaults?.[item.key];
        const status = entry?.status ?? null;
        const verified: ItemVerification = !status ? "unchecked" : status === "ok" && attestation ? "bulk" : "individual";
        const detail = entry?.status === "fault"
          ? [entry.severity ?? "Major", entry.observation].filter(Boolean).join(": ")
          : undefined;
        return { key: item.key, label: item.label, status, verified, ...(detail ? { detail } : {}) };
      });
      if (!attestation && items.every((i) => i.status === null)) continue;
      out.push({ step, key: section.key, title: section.title, attestation, items });
    }
  }
  return out;
}
