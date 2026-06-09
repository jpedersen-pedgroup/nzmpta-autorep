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
  /** Legacy Lookup category for this check's standard fault observations. When the item is marked
   * Fault, the Tester picks the specific fault from this list (see faultObservationsFor). */
  lookup?: string;
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
    { key: "vp.oilWater", label: "Oil / water condition", lookup: "VPWaterCondition" },
    { key: "vp.reservoirHeight", label: "Height of oil / water in reservoir", lookup: "VPWaterReservoir" },
    { key: "vp.supplyProtected", label: "Oil / water supply protected", lookup: "VPWaterSupplyProtected" },
    { key: "vp.wick", label: "Wick condition", lookup: "VPWickCondition" },
    { key: "vp.belt", label: "Belt condition", lookup: "VPBeltCondition" },
    { key: "vp.endPlay", label: "End play", lookup: "VPEndPlay" },
    { key: "vp.guards", label: "Guards on shaft or belts", lookup: "VPGuardsShaftBelts" },
    { key: "vp.interceptor", label: "Interceptor connection", lookup: "VPInterceptorConnection" },
    { key: "vp.exhaust", label: "Exhaust system restrictions", lookup: "VPExaustSystem" },
    { key: "vp.coupling", label: "Direct coupling condition", lookup: "VPDirectCoupling" },
    { key: "vp.beltSize", label: "Vacuum pump belt size", data: true, unit: "size" },
  ],
};

export const PRE_START_RELEASER_BELT: ChecklistSection = {
  key: "ReleaserBeltDriven",
  title: "Releaser milk pumps (belt driven)",
  items: [
    { key: "rmp.belt", label: "Belt condition", lookup: "RMPBeltCondition" },
    { key: "rmp.beltTension", label: "Belt tension", lookup: "RMPBeltTension" },
    { key: "rmp.guards", label: "Guards on shafts and belts", lookup: "RMPGuardShaftsBelts" },
    { key: "rmp.beltSize", label: "Milk pump belt size", data: true, unit: "size" },
  ],
};

export const PRE_START_RELEASER_TYPE: ChecklistSection = {
  key: "ReleaserType",
  title: "Releaser (diaphragm / centrifugal / F.I.P / lobe)",
  items: [
    { key: "rmp.intake", label: "RMP intake line", lookup: "RDCIntakeLine" },
    { key: "rmp.nrv", label: "Non return valve", lookup: "RDCNonReturnValve" },
    { key: "rmp.rotation", label: "Rotation", lookup: "RDCRotation" },
    { key: "rmp.backplate", label: "Backplate", lookup: "RDCBackplate" },
  ],
};

export const PRE_START_RELEASER_CONTROLS: ChecklistSection = {
  key: "Releasers",
  title: "Releasers",
  items: [{ key: "rmp.controls", label: "RMP controls", lookup: "RMPControls" }],
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
      { key: "ba.milkLineHeight", label: "Milk line height", lookup: "BAMilkLineHeight" },
      { key: "ba.clusterAlignment", label: "Cluster alignment", lookup: "BAClusterAlignment" },
      { key: "ba.herringboneCentres", label: "Herringbone centres", lookup: "BAHerringboneCentres" },
      { key: "ba.herringboneCentresValue", label: "Herringbone centres (mm)", data: true, unit: "mm" },
      { key: "ba.clusterPosition", label: "Cluster position", lookup: "BAClusterPosition" },
      { key: "ba.clusterPositionValue", label: "Cluster position (mm)", data: true, unit: "mm" },
      { key: "ba.firstInlet", label: "First inlet position", lookup: "BAFirstInlet" },
    ],
  },
  Rotaries: {
    key: "Rotaries",
    title: "Rotary platform",
    items: [
      { key: "rot.rotation", label: "Platform rotation", lookup: "ROTARIESRotation" },
      { key: "rot.rotationValue", label: "Rotation (speed / direction)", data: true },
      { key: "rot.clusters", label: "Clusters", lookup: "ROTARIESClusters" },
      { key: "rot.clustersValue", label: "Cluster count", data: true },
    ],
  },
  MainAirline: {
    key: "MainAirline",
    title: "Main airline",
    items: [
      { key: "ma.mounting", label: "Mounting", lookup: "MAMounting" },
      { key: "ma.movement", label: "Movement", lookup: "MAMovement" },
      { key: "ma.seals", label: "Seals & joiners", lookup: "MASealsJoiners" },
      { key: "ma.deadEnds", label: "Dead-end lengths", lookup: "MADeadEndLengths" },
      { key: "ma.diameter", label: "Diameter", lookup: "MADiameter" },
      { key: "ma.plumbing", label: "Plumbing", lookup: "MAPlubming" },
      { key: "ma.slopeDegree", label: "Slope — degree", lookup: "MASlopeDegree" },
      { key: "ma.slopeDirection", label: "Slope — direction", lookup: "MASlopeDirection" },
      { key: "ma.drainPoints", label: "Airline drain points", lookup: "MAAirlineDrainPoints" },
      { key: "ma.bendsSize", label: "Airline bends size", data: true, unit: "size" },
    ],
  },
  Inlets: {
    key: "Inlets",
    title: "Inlets",
    items: [
      { key: "in.diameter", label: "Diameter", lookup: "INLETSDiameter" },
      { key: "in.diameterValue", label: "Inlet diameter (mm)", data: true, unit: "mm" },
      { key: "in.position", label: "Position", lookup: "INLETSPosition" },
      { key: "in.alignment", label: "Alignment", lookup: "INLETSAlignment" },
      { key: "in.mouldedBendsCondition", label: "Moulded bends condition", lookup: "INLETSMouldedBendsCondition" },
      { key: "in.mouldedBendsDiameter", label: "Moulded bends diameter", lookup: "INLETSMouldedBendsDiameter" },
    ],
  },
  Clusters: {
    key: "Clusters",
    title: "Clusters",
    items: [
      { key: "cl.airAdmission", label: "Air admission", lookup: "CLUSTERSAirAdmission" },
      { key: "cl.airAdmissionDiameter", label: "Air admission diameter", lookup: "CLUSTERSAirAdmissionDiameter" },
      { key: "cl.pulseTubeManifold", label: "Pulse tube manifold", lookup: "CLUSTERSPulseTubeManifold" },
    ],
  },
  // — Running 2 —
  Claw: {
    key: "Claw",
    title: "Claw",
    items: [
      { key: "claw.type", label: "Claw type", data: true },
      { key: "claw.shellType", label: "Shell type", data: true },
      { key: "claw.condition", label: "Claw condition", lookup: "CLAWClawCondition" },
      { key: "claw.shutOffValves", label: "Shut-off valves", lookup: "CLAWShutOffValves" },
      { key: "claw.inletDiameter", label: "Claw inlet diameter" },
      { key: "claw.outletDiameter", label: "Claw outlet diameter" },
    ],
  },
  Liner: {
    key: "Liner",
    title: "Liner",
    items: [
      { key: "liner.shellCompatibility", label: "Shell compatibility", lookup: "LINERShellCompatibility" },
      { key: "liner.typeF", label: "Liner type (front)", data: true },
      { key: "liner.typeB", label: "Liner type (back)", data: true },
      { key: "liner.tension", label: "Tension", lookup: "LINERLinerTension" },
      { key: "liner.alignment", label: "Alignment", lookup: "LINERLinerAlignment" },
      { key: "liner.lipCondition", label: "Lip condition", lookup: "LINERLinerLipCondition" },
      { key: "liner.condition", label: "Condition", lookup: "LINERCondition" },
    ],
  },
  Shell: {
    key: "Shell",
    title: "Shell",
    items: [
      { key: "shell.condition", label: "Condition", lookup: "SHELLCondition" },
      { key: "shell.portCondition", label: "Port condition", lookup: "SHELLPortCondition" },
    ],
  },
  ShortPulseTube: {
    key: "ShortPulseTube",
    title: "Short pulse tube",
    items: [
      { key: "spt.condition", label: "Condition", lookup: "SPTCondition" },
      { key: "spt.length", label: "Length", lookup: "SPTLenghth" },
      { key: "spt.lengthValue", label: "Length (mm)", data: true, unit: "mm" },
      { key: "spt.diameter", label: "Diameter", lookup: "SPTDiameter" },
      { key: "spt.diameterValue", label: "Diameter (mm)", data: true, unit: "mm" },
    ],
  },
  LongPulseTube: {
    key: "LongPulseTube",
    title: "Long pulse tube",
    items: [
      { key: "lpt.condition", label: "Condition", lookup: "LPTCondition" },
      { key: "lpt.compatibility", label: "Compatibility", lookup: "LPTCompatability" },
      { key: "lpt.diameter", label: "Diameter", lookup: "LPTDiameter" },
      { key: "lpt.diameterValue", label: "Diameter (mm)", data: true, unit: "mm" },
      { key: "lpt.length", label: "Length", lookup: "LPTLength" },
      { key: "lpt.lengthValue", label: "Length (mm)", data: true, unit: "mm" },
    ],
  },
  LongMilkTube: {
    key: "LongMilkTube",
    title: "Long milk tube",
    items: [
      { key: "lmt.length", label: "Length", lookup: "LMTLength" },
      { key: "lmt.lengthValue", label: "Length (mm)", data: true, unit: "mm" },
      { key: "lmt.diameter", label: "Diameter" },
      { key: "lmt.diameterValue", label: "Diameter (mm)", data: true, unit: "mm" },
      { key: "lmt.condition", label: "Condition", lookup: "LMTCondition" },
    ],
  },
  Platform: {
    key: "Platform",
    title: "Platform",
    items: [
      { key: "plat.mounting", label: "Mounting", lookup: "PLATFORMMounting" },
      { key: "plat.slope", label: "Slope", lookup: "PLATFORMSlope" },
    ],
  },
  // — Running 3 —
  MilkFlowIndicator: {
    key: "MilkFlowIndicator",
    title: "Milk-flow indicator",
    items: [
      { key: "mfi.condition", label: "Condition", lookup: "MFICondition" },
      { key: "mfi.degree", label: "Degree", lookup: "MFIDegree" },
      { key: "mfi.installation", label: "Installation", lookup: "MFIInstallation" },
    ],
  },
  Acr: {
    key: "Acr",
    title: "ACR (automatic cluster removers)",
    items: [
      { key: "acr.airRamCondition", label: "Air ram condition", lookup: "ACRAirRamCondition" },
      { key: "acr.tubingCondition", label: "Tubing condition", lookup: "ACRTubingCondition" },
      { key: "acr.floatCondition", label: "Float condition", lookup: "ACRFloatCondition" },
    ],
  },
  MilkMeter: {
    key: "MilkMeter",
    title: "Milk meters",
    items: [
      { key: "mm.tubingCondition", label: "Tubing condition", lookup: "MMTubingCondition" },
      { key: "mm.condition", label: "Condition", lookup: "MMCondition" },
    ],
  },
  Pulsation: {
    key: "Pulsation",
    title: "Pulsation",
    items: [
      { key: "pn.filters", label: "Filters", lookup: "PULSATORSFilters" },
      { key: "pn.port", label: "Port", lookup: "PULSATORSPort" },
      { key: "pn.filteredAir", label: "Filtered air", lookup: "PULSATORSFilteredAir" },
      { key: "pn.recomPulsator", label: "Recommended pulsator fitted", lookup: "PULSATORSRecommendedPulsator" },
      { key: "pn.recomPulsatorValue", label: "Recommended pulsator", data: true },
      { key: "pn.lastService", label: "Last pulsator service", lookup: "PULSATORSLastPulsatorService" },
      { key: "pn.lastServiceValue", label: "Last service date", data: true },
      { key: "pn.runTime", label: "Pulsator run time", lookup: "PULSATORSPulsatorRunTime" },
      { key: "pn.runTimeValue", label: "Run time (hrs)", data: true, unit: "hrs" },
    ],
  },
  VacuumGauge: {
    key: "VacuumGauge",
    title: "Vacuum gauge",
    items: [
      { key: "vg.present", label: "Gauge present", lookup: "VGPresent" },
      { key: "vg.position", label: "Position", lookup: "VGPosition" },
      { key: "vg.condition", label: "Condition", lookup: "VGCondition" },
    ],
  },
  Regulator: {
    key: "Regulator",
    title: "Regulator",
    items: [
      { key: "reg.filters", label: "Filters", lookup: "MRVRFilters" },
      { key: "reg.position", label: "Position", lookup: "MRVRPosition" },
      { key: "reg.fittedSpec", label: "Fitted to specification", lookup: "MRVRFittedSpecifications" },
    ],
  },
  // — Running 4 —
  Receiver: {
    key: "Receiver",
    title: "Receiver",
    items: [
      { key: "rcv.perspexCondition", label: "Perspex condition", lookup: "RECEIVERPerspexCondition" },
      { key: "rcv.sealPlacement", label: "Seal placement", lookup: "RECEIVERSealPlacement" },
      { key: "rcv.size", label: "Size", lookup: "RECEIVERSize" },
      { key: "rcv.restrictions", label: "Restrictions", lookup: "RECEIVERRestrictions" },
    ],
  },
  VacuumPumpRunning: {
    key: "VacuumPumpRunning",
    title: "Vacuum pump (running) — oil / water",
    items: [
      { key: "vpr.interceptorMovement", label: "Interceptor movement", lookup: "VPOWInterceptorMovement" },
      { key: "vpr.noise", label: "Noise", lookup: "VPOWNoise" },
      { key: "vpr.waterFlowRate", label: "Water flow rate", lookup: "VPOWWaterFlowRate" },
      { key: "vpr.waterTemperature", label: "Water temperature", lookup: "VPOWWaterTemperature" },
    ],
  },
  Jetters: {
    key: "Jetters",
    title: "Cleaning jetters",
    items: [
      { key: "cs.jetterType", label: "Jetter type", lookup: "CSJetterType" },
      { key: "cs.jetterTypeValue", label: "Jetter type", data: true },
      { key: "cs.jetterCondition", label: "Jetter condition", lookup: "CSJetterCondition" },
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
