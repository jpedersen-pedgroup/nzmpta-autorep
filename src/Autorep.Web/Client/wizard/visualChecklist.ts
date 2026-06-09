// Visual-faults checklist definitions (condensed from the legacy VisualFaultsMMStart /
// VisualFaultsMMRunning columns). Running sections are keyed to the WizardStepResolver's
// section keys so the resolver drives which sections the Tester sees.
import type { VisualFaultEntry } from "./types";

export interface ChecklistItem {
  key: string;
  label: string;
  /** A data-capture field (size / diameter / length / run-time) rather than an OK/Fault check.
   * Rendered as a text input and stored in the test's dataFields map; excluded from completeness. */
  data?: boolean;
  /** Optional unit / placeholder hint shown for data fields. */
  unit?: string;
}
export interface ChecklistSection {
  key: string;
  title: string;
  items: ChecklistItem[];
}

// Pre-start (VisualFaultsMMStart) — "Part One: to be completed before the milking machine is started".
export const PRE_START_VACUUM_PUMP: ChecklistSection = {
  key: "VacuumPump",
  title: "Vacuum pumps (remove guard)",
  items: [
    { key: "vp.oilWater", label: "Oil / water condition" },
    { key: "vp.reservoirHeight", label: "Height of oil / water in reservoir" },
    { key: "vp.supplyProtected", label: "Oil / water supply protected" },
    { key: "vp.wick", label: "Wick condition" },
    { key: "vp.belt", label: "Belt condition" },
    { key: "vp.endPlay", label: "End play" },
    { key: "vp.guards", label: "Guards on shaft or belts" },
    { key: "vp.interceptor", label: "Interceptor connection" },
    { key: "vp.exhaust", label: "Exhaust system restrictions" },
    { key: "vp.coupling", label: "Direct coupling condition" },
    { key: "vp.beltSize", label: "Vacuum pump belt size", data: true, unit: "size" },
  ],
};

export const PRE_START_RELEASER_BELT: ChecklistSection = {
  key: "ReleaserBeltDriven",
  title: "Releaser milk pumps (belt driven)",
  items: [
    { key: "rmp.belt", label: "Belt condition" },
    { key: "rmp.beltTension", label: "Belt tension" },
    { key: "rmp.guards", label: "Guards on shafts and belts" },
    { key: "rmp.beltSize", label: "Milk pump belt size", data: true, unit: "size" },
  ],
};

export const PRE_START_RELEASER_TYPE: ChecklistSection = {
  key: "ReleaserType",
  title: "Releaser (diaphragm / centrifugal / F.I.P / lobe)",
  items: [
    { key: "rmp.intake", label: "RMP intake line" },
    { key: "rmp.nrv", label: "Non return valve" },
    { key: "rmp.rotation", label: "Rotation" },
    { key: "rmp.backplate", label: "Backplate" },
  ],
};

export const PRE_START_RELEASER_CONTROLS: ChecklistSection = {
  key: "Releasers",
  title: "Releasers",
  items: [{ key: "rmp.controls", label: "RMP controls" }],
};

export const RUNNING_SECTIONS: Record<string, ChecklistSection> = {
  BailArea: {
    key: "BailArea",
    title: "Bail area",
    items: [
      { key: "ba.milkLineHeight", label: "Milk line height" },
      { key: "ba.clusterAlignment", label: "Cluster alignment" },
      { key: "ba.herringboneCentres", label: "Herringbone centres" },
      { key: "ba.clusterPosition", label: "Cluster position" },
      { key: "ba.firstInlet", label: "First inlet position" },
    ],
  },
  Rotaries: {
    key: "Rotaries",
    title: "Rotary platform",
    items: [
      { key: "rot.rotation", label: "Platform rotation" },
      { key: "rot.clusters", label: "Clusters" },
    ],
  },
  MainAirline: {
    key: "MainAirline",
    title: "Main airline",
    items: [
      { key: "ma.mounting", label: "Mounting" },
      { key: "ma.movement", label: "Movement" },
      { key: "ma.seals", label: "Seals & joiners" },
      { key: "ma.deadEnds", label: "Dead-end lengths" },
      { key: "ma.diameter", label: "Diameter" },
      { key: "ma.slope", label: "Slope (degree & direction)" },
      { key: "ma.drainPoints", label: "Airline drain points" },
    ],
  },
  Inlets: {
    key: "Inlets",
    title: "Inlets",
    items: [
      { key: "in.diameter", label: "Diameter" },
      { key: "in.position", label: "Position" },
      { key: "in.alignment", label: "Alignment" },
      { key: "in.mouldedBends", label: "Moulded bends condition" },
    ],
  },
  Clusters: {
    key: "Clusters",
    title: "Clusters",
    items: [
      { key: "cl.airAdmission", label: "Air admission" },
      { key: "cl.airAdmissionDiameter", label: "Air admission diameter" },
      { key: "cl.pulseTubeManifolds", label: "Pulse tube manifolds" },
    ],
  },
};

export function preStartSections(hasReleaserPump: boolean): ChecklistSection[] {
  return hasReleaserPump
    ? [PRE_START_VACUUM_PUMP, PRE_START_RELEASER_BELT, PRE_START_RELEASER_TYPE, PRE_START_RELEASER_CONTROLS]
    : [PRE_START_VACUUM_PUMP];
}

/** Maps the resolver's VisualFaultsRunning section keys to checklist sections. */
export function runningSectionsFor(sectionKeys: string[]): ChecklistSection[] {
  return sectionKeys
    .map((k) => RUNNING_SECTIONS[k])
    .filter((s): s is ChecklistSection => Boolean(s));
}

/** A checklist is complete when every visible OK/Fault item has a status. Data-capture fields
 * (sizes, diameters) are optional and don't gate completeness. */
export function checklistComplete(
  sections: ChecklistSection[],
  entries: Record<string, VisualFaultEntry>,
): boolean {
  return (
    sections.length > 0 &&
    sections.every((sec) => sec.items.filter((it) => !it.data).every((it) => entries[it.key]?.status !== undefined))
  );
}

/** "Check all as verified": set every non-fault check item to OK (keeps already-logged faults;
 * leaves data fields untouched). */
export function applyCheckAll(
  sections: ChecklistSection[],
  entries: Record<string, VisualFaultEntry>,
): Record<string, VisualFaultEntry> {
  const next = { ...entries };
  for (const sec of sections) {
    for (const it of sec.items) {
      if (it.data) continue;
      if (next[it.key]?.status !== "fault") next[it.key] = { status: "ok" };
    }
  }
  return next;
}
