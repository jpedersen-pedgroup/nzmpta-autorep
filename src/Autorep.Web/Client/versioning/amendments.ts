// Amendment engine for re-edited (versioned) tests: computes the human-readable field-level
// difference between a superseded version and its replacement at sign-off. The result is stored
// on the new version (LocalTest.amendments) so it round-trips through PayloadJson and the
// report's "Amendment history" page reprints identically on any device — the diff is FIXED at
// sign-off, not recomputed against whatever data a later device happens to hold.
import type { AmendmentRecord, FieldChange, LocalTest, MeasurementRow } from "../db/testStore";
import type { MachineConfiguration } from "../wizard/types";
import { allReadingSections } from "../passfail/standards";
import { preStartSections, RUNNING_SECTIONS } from "../wizard/visualChecklist";

// Report-facing section names, in the order changes are listed.
const S_FARM = "Farm";
const S_CONFIG = "Machine configuration";
const S_READINGS = "Numerical readings";
const S_PULSATORS = "Pulsator test results";
const S_CLUSTERS = "Individual cluster tests";
const S_VISUAL = "Visual checks";
const S_DATA = "Recorded measurements";
const S_RECS = "Recommendations";
const S_OTHER = "Other";
const SECTION_ORDER = [S_FARM, S_CONFIG, S_READINGS, S_PULSATORS, S_CLUSTERS, S_VISUAL, S_DATA, S_RECS, S_OTHER];

const CONFIG_LABELS: Record<keyof MachineConfiguration, string> = {
  plantType: "Plant type",
  plantSize: "Plant size",
  clusterCount: "Clusters",
  herdSize: "Herd size",
  lastBmcc: "Last BMCC",
  milklineSize: "Milkline size (mm)",
  atmosPressureSeaLevel: "Atmos. pressure at sea level (kPa)",
  flushingPulsationSystem: "Flushing pulsation system",
  pulsatorBrand: "Pulsator brand",
  pulsatorModel: "Pulsator model",
  pulsatorConfiguration: "Pulsator configuration",
  pulsatorCount: "Pulsators",
  clawModel: "Claw",
  shellModel: "Shell",
  linerModel: "Liner (front)",
  backLiner: "Liner (back)",
  linerVented: "Vented liners",
  numberOfVacuumPumps: "Vacuum pumps",
  pumpLubrication: "Pump lubrication",
  vsdFitted: "VSD fitted",
  isoPortsAvailable: "ISO ports available",
  hasPulsatorStopSystem: "Pulsator stop system",
  hasAcr: "ACRs",
  hasBailGates: "Bail gates",
  hasMilkMeters: "Milk meters",
  hasTeatSprayer: "Teat sprayer",
  hasBackingGate: "Backing gate",
  hasReleaserPump: "Releaser pump",
};

const PULSATOR_COLS: Record<string, string> = {
  rate: "Rate (ppm)",
  ratioFront: "Ratio F (%)",
  ratioBack: "Ratio B (%)",
  phaseB: "Phase b (%)",
  phaseDms: "Phase d (ms)",
  maxVacuum: "Max vac (kPa)",
  limp: "Limp (%)",
};

const CLUSTER_COLS: Record<string, string> = {
  totalAirAdmission: "Total air admission",
  leakage: "Leakage",
  airVent: "Air-vent admission",
};

function fmt(v: unknown): string {
  if (v === null || v === undefined) return "—";
  if (typeof v === "boolean") return v ? "Yes" : "No";
  const s = String(v).trim();
  return s.length === 0 ? "—" : s;
}

/** key → "Section title · Item label" across every checklist item that can exist (both pre-start
 * variants + every running section), so labels resolve even after a config change moved the plan. */
function visualLabelMap(): Map<string, string> {
  const map = new Map<string, string>();
  for (const sec of [...preStartSections(true), ...Object.values(RUNNING_SECTIONS)]) {
    for (const it of sec.items) map.set(it.key, `${sec.title} · ${it.label}`);
  }
  return map;
}

/** key → { label, unit } for every reading either version could have shown. */
function readingLabelMap(base: LocalTest, edited: LocalTest): Map<string, { label: string; unit: string }> {
  const merged = { ...base.readings, ...edited.readings };
  const map = new Map<string, { label: string; unit: string }>();
  for (const cfg of [base.config, edited.config]) {
    for (const sec of allReadingSections(cfg, merged)) {
      for (const r of sec.readings) if (!map.has(r.key)) map.set(r.key, { label: r.label, unit: r.unit });
    }
  }
  return map;
}

function diffRecord(
  section: string,
  before: Record<string, unknown> | undefined,
  after: Record<string, unknown> | undefined,
  labelFor: (key: string) => string,
  format: (key: string, v: unknown) => string = (_k, v) => fmt(v),
): FieldChange[] {
  const out: FieldChange[] = [];
  const keys = new Set([...Object.keys(before ?? {}), ...Object.keys(after ?? {})]);
  for (const key of keys) {
    const from = format(key, before?.[key]);
    const to = format(key, after?.[key]);
    if (from !== to) out.push({ section, label: labelFor(key), from, to });
  }
  return out;
}

function rowSummary(values: Record<string, string>, cols: Record<string, string>): string {
  const parts = Object.entries(values)
    .filter(([, v]) => (v ?? "").trim().length > 0)
    .map(([k, v]) => `${cols[k] ?? k} ${v}`);
  return parts.length > 0 ? parts.join(", ") : "no values";
}

/** Per-unit row diff: rows are matched by id first (edits keep ids), then by unit label (covers
 * a delete-and-retype of the same unit); leftovers are reported as added/removed with a compact
 * value summary so the audit page stands alone. */
function diffRows(
  section: string,
  rowNoun: string,
  before: MeasurementRow[] | undefined,
  after: MeasurementRow[] | undefined,
  cols: Record<string, string>,
): FieldChange[] {
  const out: FieldChange[] = [];
  const b = [...(before ?? [])];
  const a = [...(after ?? [])];

  const pairs: Array<[MeasurementRow, MeasurementRow]> = [];
  for (const row of b) {
    const byId = a.findIndex((x) => x.id === row.id);
    if (byId >= 0) pairs.push([row, a.splice(byId, 1)[0]]);
  }
  const unmatchedB: MeasurementRow[] = [];
  for (const row of b.filter((x) => !pairs.some(([pb]) => pb === x))) {
    const byUnit = a.findIndex((x) => x.unit === row.unit);
    if (byUnit >= 0) pairs.push([row, a.splice(byUnit, 1)[0]]);
    else unmatchedB.push(row);
  }

  for (const [prev, next] of pairs) {
    if (prev.unit !== next.unit) {
      out.push({ section, label: `${rowNoun} ${prev.unit}`, from: `Unit ${prev.unit}`, to: `Unit ${next.unit}` });
    }
    out.push(
      ...diffRecord(section, prev.values, next.values, (k) => `${rowNoun} ${next.unit} · ${cols[k] ?? k}`),
    );
  }
  for (const row of unmatchedB) {
    out.push({ section, label: `${rowNoun} ${row.unit}`, from: rowSummary(row.values, cols), to: "Removed" });
  }
  for (const row of a) {
    out.push({ section, label: `${rowNoun} ${row.unit}`, from: "—", to: `Added (${rowSummary(row.values, cols)})` });
  }
  return out;
}

// Every component (severity, observation AND note) participates independently, so an edit to
// any one of them — e.g. changing just the free-text note under an unchanged observation —
// always produces a different formatted string and therefore a recorded change.
function fmtVisual(entry: LocalTest["visualFaults"][string] | undefined): string {
  if (!entry) return "Not checked";
  if (entry.status === "ok") return "OK";
  let s = `Fault — ${entry.severity ?? "Major"}`;
  if (entry.observation) s += `: ${entry.observation}`;
  if (entry.note && entry.note !== entry.observation) s += ` (note: ${entry.note})`;
  return s;
}

// Includes size and attach time so replacing the attachment with a SAME-NAMED corrected export
// (analyser software exports under a fixed filename) is still recorded.
function fmtAttachment(p: LocalTest["pulsationPdf"]): string | undefined {
  if (!p) return undefined;
  const at = p.attachedAt.slice(0, 16).replace("T", " ");
  return `${p.name} (${Math.max(1, Math.round(p.size / 1024))} KB, attached ${at})`;
}

/** All field-level differences between a superseded version and its edited replacement. */
export function computeChanges(base: LocalTest, edited: LocalTest): FieldChange[] {
  const changes: FieldChange[] = [];

  if (fmt(base.farmName) !== fmt(edited.farmName)) {
    changes.push({ section: S_FARM, label: "Farm", from: fmt(base.farmName), to: fmt(edited.farmName) });
  }

  for (const key of Object.keys(CONFIG_LABELS) as (keyof MachineConfiguration)[]) {
    const from = fmt(base.config[key]);
    const to = fmt(edited.config[key]);
    if (from !== to) changes.push({ section: S_CONFIG, label: CONFIG_LABELS[key], from, to });
  }

  const readingLabels = readingLabelMap(base, edited);
  changes.push(
    ...diffRecord(
      S_READINGS,
      base.readings,
      edited.readings,
      (k) => readingLabels.get(k)?.label ?? k,
      (k, v) => (v == null ? "—" : `${v}${readingLabels.get(k)?.unit ? ` ${readingLabels.get(k)!.unit}` : ""}`),
    ),
  );

  changes.push(...diffRows(S_PULSATORS, "Pulsator", base.pulsatorRows, edited.pulsatorRows, PULSATOR_COLS));
  changes.push(...diffRows(S_CLUSTERS, "Cluster", base.clusterRows, edited.clusterRows, CLUSTER_COLS));

  const visualLabels = visualLabelMap();
  const visualKeys = new Set([...Object.keys(base.visualFaults), ...Object.keys(edited.visualFaults)]);
  for (const key of visualKeys) {
    const from = fmtVisual(base.visualFaults[key]);
    const to = fmtVisual(edited.visualFaults[key]);
    if (from !== to) changes.push({ section: S_VISUAL, label: visualLabels.get(key) ?? key, from, to });
  }
  // guardsOnPulsators is the one optional boolean: absent and explicit-false render identically
  // in the UI, so normalise before comparing — absent → false is not an amendment.
  if (fmt(base.guardsOnPulsators ?? false) !== fmt(edited.guardsOnPulsators ?? false)) {
    changes.push({
      section: S_VISUAL,
      label: "Guards installed on pulsators",
      from: fmt(base.guardsOnPulsators ?? false),
      to: fmt(edited.guardsOnPulsators ?? false),
    });
  }

  changes.push(...diffRecord(S_DATA, base.dataFields, edited.dataFields, (k) => visualLabels.get(k) ?? k));
  changes.push(
    ...diffRecord(
      S_RECS,
      base.recommendations,
      edited.recommendations,
      (k) => visualLabels.get(k) ?? readingLabels.get(k)?.label ?? k,
    ),
  );

  const other: Array<[string, unknown, unknown]> = [
    ["Tester comment", base.notes, edited.notes],
    ["Calibration expiry — airflow meters", base.calAirFlowMeters, edited.calAirFlowMeters],
    ["Calibration expiry — pulsator testers", base.calPulsatorTesters, edited.calPulsatorTesters],
    ["Calibration expiry — vacuum gauges", base.calVacuumGauges, edited.calVacuumGauges],
    ["Pulsation analyser attachment", fmtAttachment(base.pulsationPdf), fmtAttachment(edited.pulsationPdf)],
  ];
  for (const [label, from, to] of other) {
    if (fmt(from) !== fmt(to)) changes.push({ section: S_OTHER, label, from: fmt(from), to: fmt(to) });
  }

  return changes.sort((x, y) => SECTION_ORDER.indexOf(x.section) - SECTION_ORDER.indexOf(y.section));
}

/** The amendment record appended to the superseding version at sign-off. When the superseded
 * version isn't on this device (shouldn't happen — versions are created where the original
 * lives), the record still lands, flagged baseUnavailable, so the audit trail never has a
 * silent hole. */
export function buildAmendmentRecord(
  base: LocalTest | undefined,
  edited: LocalTest,
  amendedAt: string,
  amendedBy?: string | null,
): AmendmentRecord {
  const version = edited.version ?? 2;
  return {
    version,
    amendedAt,
    ...(amendedBy ? { amendedBy } : {}),
    baseVersion: base?.version ?? version - 1,
    baseCompletedAt: base?.markedCompleteAt ?? null,
    changes: base ? computeChanges(base, edited) : [],
    ...(base ? {} : { baseUnavailable: true as const }),
  };
}
