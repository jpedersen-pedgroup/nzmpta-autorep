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

// Running (VisualFaultsMMRunning1–4) — "Part Two". The resolver decides which sections are shown
// (bail vs rotary; ACR / milk-meter only when fitted). Items map 1:1 to the legacy *E columns;
// *Input/*Size columns become data-capture fields.
export const RUNNING_SECTIONS: Record<string, ChecklistSection> = {
  // — Running 1 —
  BailArea: {
    key: "BailArea",
    title: "Bail area",
    items: [
      { key: "ba.milkLineHeight", label: "Milk line height" },
      { key: "ba.clusterAlignment", label: "Cluster alignment" },
      { key: "ba.herringboneCentres", label: "Herringbone centres" },
      { key: "ba.herringboneCentresValue", label: "Herringbone centres (mm)", data: true, unit: "mm" },
      { key: "ba.clusterPosition", label: "Cluster position" },
      { key: "ba.clusterPositionValue", label: "Cluster position (mm)", data: true, unit: "mm" },
      { key: "ba.firstInlet", label: "First inlet position" },
    ],
  },
  Rotaries: {
    key: "Rotaries",
    title: "Rotary platform",
    items: [
      { key: "rot.rotation", label: "Platform rotation" },
      { key: "rot.rotationValue", label: "Rotation (speed / direction)", data: true },
      { key: "rot.clusters", label: "Clusters" },
      { key: "rot.clustersValue", label: "Cluster count", data: true },
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
      { key: "ma.plumbing", label: "Plumbing" },
      { key: "ma.slopeDegree", label: "Slope — degree" },
      { key: "ma.slopeDirection", label: "Slope — direction" },
      { key: "ma.drainPoints", label: "Airline drain points" },
      { key: "ma.bendsSize", label: "Airline bends size", data: true, unit: "size" },
    ],
  },
  Inlets: {
    key: "Inlets",
    title: "Inlets",
    items: [
      { key: "in.diameter", label: "Diameter" },
      { key: "in.diameterValue", label: "Inlet diameter (mm)", data: true, unit: "mm" },
      { key: "in.position", label: "Position" },
      { key: "in.alignment", label: "Alignment" },
      { key: "in.mouldedBendsCondition", label: "Moulded bends condition" },
      { key: "in.mouldedBendsDiameter", label: "Moulded bends diameter" },
    ],
  },
  Clusters: {
    key: "Clusters",
    title: "Clusters",
    items: [
      { key: "cl.airAdmission", label: "Air admission" },
      { key: "cl.airAdmissionDiameter", label: "Air admission diameter" },
      { key: "cl.pulseTubeManifold", label: "Pulse tube manifold" },
    ],
  },
  // — Running 2 —
  Claw: {
    key: "Claw",
    title: "Claw",
    items: [
      { key: "claw.type", label: "Claw type", data: true },
      { key: "claw.shellType", label: "Shell type", data: true },
      { key: "claw.condition", label: "Claw condition" },
      { key: "claw.shutOffValves", label: "Shut-off valves" },
      { key: "claw.inletDiameter", label: "Claw inlet diameter" },
      { key: "claw.outletDiameter", label: "Claw outlet diameter" },
    ],
  },
  Liner: {
    key: "Liner",
    title: "Liner",
    items: [
      { key: "liner.shellCompatibility", label: "Shell compatibility" },
      { key: "liner.typeF", label: "Liner type (front)", data: true },
      { key: "liner.typeB", label: "Liner type (back)", data: true },
      { key: "liner.tension", label: "Tension" },
      { key: "liner.alignment", label: "Alignment" },
      { key: "liner.lipCondition", label: "Lip condition" },
      { key: "liner.condition", label: "Condition" },
    ],
  },
  Shell: {
    key: "Shell",
    title: "Shell",
    items: [
      { key: "shell.condition", label: "Condition" },
      { key: "shell.portCondition", label: "Port condition" },
    ],
  },
  ShortPulseTube: {
    key: "ShortPulseTube",
    title: "Short pulse tube",
    items: [
      { key: "spt.condition", label: "Condition" },
      { key: "spt.length", label: "Length" },
      { key: "spt.lengthValue", label: "Length (mm)", data: true, unit: "mm" },
      { key: "spt.diameter", label: "Diameter" },
      { key: "spt.diameterValue", label: "Diameter (mm)", data: true, unit: "mm" },
    ],
  },
  LongPulseTube: {
    key: "LongPulseTube",
    title: "Long pulse tube",
    items: [
      { key: "lpt.condition", label: "Condition" },
      { key: "lpt.compatibility", label: "Compatibility" },
      { key: "lpt.diameter", label: "Diameter" },
      { key: "lpt.diameterValue", label: "Diameter (mm)", data: true, unit: "mm" },
      { key: "lpt.length", label: "Length" },
      { key: "lpt.lengthValue", label: "Length (mm)", data: true, unit: "mm" },
    ],
  },
  LongMilkTube: {
    key: "LongMilkTube",
    title: "Long milk tube",
    items: [
      { key: "lmt.length", label: "Length" },
      { key: "lmt.lengthValue", label: "Length (mm)", data: true, unit: "mm" },
      { key: "lmt.diameter", label: "Diameter" },
      { key: "lmt.diameterValue", label: "Diameter (mm)", data: true, unit: "mm" },
      { key: "lmt.condition", label: "Condition" },
    ],
  },
  Platform: {
    key: "Platform",
    title: "Platform",
    items: [
      { key: "plat.mounting", label: "Mounting" },
      { key: "plat.slope", label: "Slope" },
    ],
  },
  // — Running 3 —
  MilkFlowIndicator: {
    key: "MilkFlowIndicator",
    title: "Milk-flow indicator",
    items: [
      { key: "mfi.condition", label: "Condition" },
      { key: "mfi.degree", label: "Degree" },
      { key: "mfi.installation", label: "Installation" },
    ],
  },
  Acr: {
    key: "Acr",
    title: "ACR (automatic cluster removers)",
    items: [
      { key: "acr.airRamCondition", label: "Air ram condition" },
      { key: "acr.tubingCondition", label: "Tubing condition" },
      { key: "acr.floatCondition", label: "Float condition" },
    ],
  },
  MilkMeter: {
    key: "MilkMeter",
    title: "Milk meters",
    items: [
      { key: "mm.tubingCondition", label: "Tubing condition" },
      { key: "mm.condition", label: "Condition" },
    ],
  },
  Pulsation: {
    key: "Pulsation",
    title: "Pulsation",
    items: [
      { key: "pn.filters", label: "Filters" },
      { key: "pn.port", label: "Port" },
      { key: "pn.filteredAir", label: "Filtered air" },
      { key: "pn.recomPulsator", label: "Recommended pulsator fitted" },
      { key: "pn.recomPulsatorValue", label: "Recommended pulsator", data: true },
      { key: "pn.lastService", label: "Last pulsator service" },
      { key: "pn.lastServiceValue", label: "Last service date", data: true },
      { key: "pn.runTime", label: "Pulsator run time" },
      { key: "pn.runTimeValue", label: "Run time (hrs)", data: true, unit: "hrs" },
    ],
  },
  VacuumGauge: {
    key: "VacuumGauge",
    title: "Vacuum gauge",
    items: [
      { key: "vg.present", label: "Gauge present" },
      { key: "vg.position", label: "Position" },
      { key: "vg.condition", label: "Condition" },
    ],
  },
  Regulator: {
    key: "Regulator",
    title: "Regulator",
    items: [
      { key: "reg.filters", label: "Filters" },
      { key: "reg.position", label: "Position" },
      { key: "reg.fittedSpec", label: "Fitted to specification" },
    ],
  },
  // — Running 4 —
  Receiver: {
    key: "Receiver",
    title: "Receiver",
    items: [
      { key: "rcv.perspexCondition", label: "Perspex condition" },
      { key: "rcv.sealPlacement", label: "Seal placement" },
      { key: "rcv.size", label: "Size" },
      { key: "rcv.restrictions", label: "Restrictions" },
    ],
  },
  VacuumPumpRunning: {
    key: "VacuumPumpRunning",
    title: "Vacuum pump (running) — oil / water",
    items: [
      { key: "vpr.interceptorMovement", label: "Interceptor movement" },
      { key: "vpr.noise", label: "Noise" },
      { key: "vpr.waterFlowRate", label: "Water flow rate" },
      { key: "vpr.waterTemperature", label: "Water temperature" },
    ],
  },
  Jetters: {
    key: "Jetters",
    title: "Cleaning jetters",
    items: [
      { key: "cs.jetterType", label: "Jetter type" },
      { key: "cs.jetterTypeValue", label: "Jetter type", data: true },
      { key: "cs.jetterCondition", label: "Jetter condition" },
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
