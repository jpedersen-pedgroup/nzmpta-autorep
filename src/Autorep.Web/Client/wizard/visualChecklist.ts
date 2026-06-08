// Visual-faults checklist definitions (condensed from the legacy VisualFaultsMMStart /
// VisualFaultsMMRunning columns). Running sections are keyed to the WizardStepResolver's
// section keys so the resolver drives which sections the Tester sees.
import type { VisualFaultEntry } from "./types";

export interface ChecklistItem {
  key: string;
  label: string;
}
export interface ChecklistSection {
  key: string;
  title: string;
  items: ChecklistItem[];
}

export const PRE_START_VACUUM_PUMP: ChecklistSection = {
  key: "VacuumPump",
  title: "Vacuum pump",
  items: [
    { key: "vp.oilWater", label: "Oil / water condition" },
    { key: "vp.reservoirHeight", label: "Oil / water reservoir height" },
    { key: "vp.wick", label: "Wick condition" },
    { key: "vp.belt", label: "Drive belt condition" },
    { key: "vp.endPlay", label: "End play" },
    { key: "vp.guards", label: "Guards on shafts & belts" },
    { key: "vp.interceptor", label: "Interceptor connection" },
    { key: "vp.exhaust", label: "Exhaust restrictions" },
    { key: "vp.coupling", label: "Direct coupling condition" },
  ],
};

export const PRE_START_RELEASER_PUMP: ChecklistSection = {
  key: "ReleaserMilkPump",
  title: "Releaser milk pump",
  items: [
    { key: "rmp.belt", label: "Drive belt condition" },
    { key: "rmp.beltTension", label: "Belt tension" },
    { key: "rmp.guards", label: "Guards on shafts & belts" },
    { key: "rmp.controls", label: "Controls" },
    { key: "rmp.intake", label: "Intake line" },
    { key: "rmp.nrv", label: "Non-return valve" },
    { key: "rmp.rotation", label: "Rotation" },
    { key: "rmp.backplate", label: "Backplate" },
  ],
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
    ? [PRE_START_VACUUM_PUMP, PRE_START_RELEASER_PUMP]
    : [PRE_START_VACUUM_PUMP];
}

/** Maps the resolver's VisualFaultsRunning section keys to checklist sections. */
export function runningSectionsFor(sectionKeys: string[]): ChecklistSection[] {
  return sectionKeys
    .map((k) => RUNNING_SECTIONS[k])
    .filter((s): s is ChecklistSection => Boolean(s));
}

/** A checklist is complete when every visible item has a status (OK or fault). */
export function checklistComplete(
  sections: ChecklistSection[],
  entries: Record<string, VisualFaultEntry>,
): boolean {
  return sections.length > 0 && sections.every((sec) => sec.items.every((it) => entries[it.key]?.status !== undefined));
}

/** "Check all as verified": set every non-fault item to OK (keeps already-logged faults). */
export function applyCheckAll(
  sections: ChecklistSection[],
  entries: Record<string, VisualFaultEntry>,
): Record<string, VisualFaultEntry> {
  const next = { ...entries };
  for (const sec of sections) {
    for (const it of sec.items) {
      if (next[it.key]?.status !== "fault") next[it.key] = { status: "ok" };
    }
  }
  return next;
}
