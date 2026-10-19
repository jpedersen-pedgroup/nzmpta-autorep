// Typed access to the reference catalogues extracted from the legacy Autorep database by
// tools/reference-data/extract_legacy_reference.py — see the "Legacy reference-data audit" in
// plans/reference/standards-audit.md. These are the per-model numbers the manual carries only as
// image-only scans; the legacy app judged tests against them (tick/cross columns in
// PulsationSystemResultRange and MMAdditionalTR), so wiring them in restores legacy parity.
//
// The pulsator and vacuum pump specs are now the OEM Specs the SuperAdmin maintains (synced from
// /api/equipment, see catalogOverrides.ts). The bundled files below seeded them, once, and stay as
// the fallback before a device's first sync and for a recorded model the synced set doesn't have.
// Edit specs on the OEM Specs pages, not here: these files are the legacy baseline.
import pulsatorBandsJson from "./pulsatorBands.json";
import vacuumPumpsJson from "./vacuumPumps.json";
import milkPumpsJson from "./milkPumps.json";
import releaserJson from "./releaserSpeedPower.json";
import reserveReceiverJson from "./reserveReceiver.json";
import { syncedSpecItems, type EquipmentDto } from "./catalogOverrides";

/** A pulsator model's OEM spec (legacy `Pulsator`): rate in ppm, ratios and phases in %. Only the
 * rate and ratio bands are judged; the rest is held for reference. A synced spec can leave any figure
 * blank (null); every bundled row has them all. The same names as Domain/Entities/OemSpecs.cs. */
export interface PulsatorSpec {
  rateMin: number | null;
  rateMax: number | null;
  ratioMin: number | null;
  ratioMax: number | null;
  phaseB: number | null;
  phaseD: number | null;
  maxChamberVacuum: number | null;
  recommendedRate: number | null;
  recommendedRatio: number | null;
  /** Free text ("1", "1 or 2", "2 (2x2)"). */
  maxClustersPerPulsator?: string | null;
}

/** Per-model pulsation bands: a pulsator model and its OEM spec. */
export interface PulsatorBand extends PulsatorSpec {
  name: string;
  brand: string;
}

/** A vacuum pump's OEM spec (legacy `VPModel`). `airFlow` is the curve: L/min of air per rpm at
 * 50 kPa - a De Laval DVP1600 is 1.15 x 1400 rpm = 1610 L/min, its nameplate figure. */
export interface VacuumPumpSpec {
  minRpm: number | null;
  maxRpm: number | null;
  airFlow: number | null;
  motorSizeFactor: number | null;
  waterFlowRate: number | null;
}

/** OEM vacuum-pump curve rows, joined to the pump make/model captured in the machine configuration
 * (see vacuumPumpFor). */
export interface VacuumPumpModel extends VacuumPumpSpec {
  make: string;
  model: string;
}

/** Milk-pump sizing rows (legacy `MilkPumps`). Awaiting the same config fields as VacuumPumpModel. */
export interface MilkPumpModel {
  make: string;
  model: string;
  minValue: number | null;
  maxValue: number | null;
  size: number | null;
  motor: number | null;
}

/** Required receiver reserve by milkline diameter × working-vacuum band (legacy `ReserveReceiver`).
 * Not wired: this looks like the 6b (reserve off cluster) standard, but the audit's open question
 * on 6a/6b acceptance limits is unresolved — confirm with MPNZ before turning it into a rule. */
export interface ReceiverReserveRow {
  milklineDiameter: number;
  workingVacuum: string;
  requiredReserve: number;
}

export const VACUUM_PUMPS: VacuumPumpModel[] = vacuumPumpsJson as VacuumPumpModel[];
export const MILK_PUMPS: MilkPumpModel[] = milkPumpsJson as MilkPumpModel[];
export const RECEIVER_RESERVE: ReceiverReserveRow[] = reserveReceiverJson as ReceiverReserveRow[];

const collapse = (s: string) => s.replace(/\s+/g, " ").trim();
const loose = (s: string | null | undefined) => collapse(s ?? "").toLowerCase();
const num = (v: unknown): number | null => (typeof v === "number" && Number.isFinite(v) ? v : null);

/** The synced item a test's equipment refers to: by its id first, so a renamed item still matches;
 * then by name. Pumps need the make too, since makes share model names; for a pulsator the brand only
 * breaks a tie. Null when the synced set doesn't have it. */
function syncedItem(
  items: EquipmentDto[],
  equipmentId: string | null | undefined,
  brand: string | null | undefined,
  name: string,
  brandRequired: boolean,
): EquipmentDto | null {
  if (equipmentId) {
    const byId = items.find((i) => i.id === equipmentId);
    if (byId) return byId;
  }
  const named = items.filter((i) => loose(i.name) === loose(name));
  const sameBrand = named.find((i) => loose(i.brand) === loose(brand));
  if (brandRequired) return sameBrand ?? null;
  return sameBrand ?? (named.length === 1 ? named[0] : null);
}

/** A pulsator spec read from a synced item (figures it doesn't carry are null). */
export function pulsatorSpecOf(spec: Record<string, unknown>): PulsatorSpec {
  return {
    rateMin: num(spec.rateMin),
    rateMax: num(spec.rateMax),
    ratioMin: num(spec.ratioMin),
    ratioMax: num(spec.ratioMax),
    phaseB: num(spec.phaseB),
    phaseD: num(spec.phaseD),
    maxChamberVacuum: num(spec.maxChamberVacuum),
    recommendedRate: num(spec.recommendedRate),
    recommendedRatio: num(spec.recommendedRatio),
    maxClustersPerPulsator: typeof spec.maxClustersPerPulsator === "string" ? spec.maxClustersPerPulsator : null,
  };
}

/** A vacuum pump spec read from a synced item. */
export function vacuumPumpSpecOf(spec: Record<string, unknown>): VacuumPumpSpec {
  return {
    minRpm: num(spec.minRpm),
    maxRpm: num(spec.maxRpm),
    airFlow: num(spec.airFlow),
    motorSizeFactor: num(spec.motorSizeFactor),
    waterFlowRate: num(spec.waterFlowRate),
  };
}

/** True when the spec carries a rate or a ratio band to judge against. A handful of legacy rows carry
 * zeros, and a 0-0 band would fail every reading rather than mean "no standard". */
export function hasPulsatorBand(spec: PulsatorSpec): boolean {
  const rate = spec.rateMin != null && spec.rateMax != null && spec.rateMax > 0;
  const ratio = spec.ratioMin != null && spec.ratioMax != null && spec.ratioMax > 0;
  return rate || ratio;
}

/** True when the spec carries the airflow figure the OEM capacity is worked out from. */
export function hasPumpCurve(spec: VacuumPumpSpec): boolean {
  return spec.airFlow != null && spec.airFlow > 0;
}

// The catalogue the wizard's model dropdown ships (pulsators.json) and the bands file are both
// extracted from the same legacy table with whitespace collapsed, so names join exactly; the
// normalised map guards against stray spacing in stored tests.
const bandsByName = new Map<string, PulsatorBand>(
  (pulsatorBandsJson as PulsatorBand[]).map((b) => [collapse(b.name), b]),
);

/** The current OEM spec for a pulsator model, or null when the model is unknown/unset or its spec has
 * no band (see hasPulsatorBand). The synced OEM Specs answer when they have the model - by id, then
 * name - even when its spec is empty; otherwise the bundled legacy band for that name. A test that was
 * signed off against a spec reads it through oemSpecs.ts instead. */
export function pulsatorBandFor(
  model: string | null | undefined,
  brand?: string | null,
  equipmentId?: string | null,
): PulsatorBand | null {
  if (!model) return null;
  const synced = syncedSpecItems("Pulsator");
  const item = synced ? syncedItem(synced, equipmentId, brand, model, false) : null;
  const band: PulsatorBand | null = item
    ? item.spec
      ? { name: item.name, brand: item.brand ?? "", ...pulsatorSpecOf(item.spec) }
      : null
    : (bandsByName.get(collapse(model)) ?? null);
  return band && hasPulsatorBand(band) ? band : null;
}

const pumpKey = (make: string, model: string) => `${collapse(make).toLowerCase()}|${collapse(model).toLowerCase()}`;
const pumpsByMakeModel = new Map<string, VacuumPumpModel>(
  VACUUM_PUMPS.map((p) => [pumpKey(p.make, p.model), p]),
);

/** The current OEM spec for a vacuum pump, or null when the make/model isn't in the OEM Specs (a pump
 * typed in by hand) or carries no usable airflow figure. The synced OEM Specs answer first, as for
 * pulsators; then the bundled legacy curve. Matched case- and spacing-insensitively: the make may
 * have been typed rather than picked. */
export function vacuumPumpFor(
  make: string | null | undefined,
  model: string | null | undefined,
  equipmentId?: string | null,
): VacuumPumpModel | null {
  if (!make || !model) return null;
  const synced = syncedSpecItems("VacuumPump");
  const item = synced ? syncedItem(synced, equipmentId, make, model, true) : null;
  const pump: VacuumPumpModel | null = item
    ? item.spec
      ? { make: item.brand ?? make, model: item.name, ...vacuumPumpSpecOf(item.spec) }
      : null
    : (pumpsByMakeModel.get(pumpKey(make, model)) ?? null);
  return pump && hasPumpCurve(pump) ? pump : null;
}

/** The OEM capacity (L/min at 50 kPa) of a pump running at `rpm` - the catalogue curve is linear
 * through the origin, so it is the per-rpm airflow x the speed the capacity was measured at. */
export function oemPumpCapacity(pump: VacuumPumpModel, rpm: number): number {
  return (pump.airFlow ?? 0) * rpm;
}

interface ReleaserRow {
  clusters: string;
  heads: string;
  power: string;
  minSpeed: string;
}

/** Minimum releaser speed/power (legacy `MinSpeedPowerCal`), keyed by cluster count × number of
 * heads. Exact-match only: the legacy table covers 6–40 clusters, and plants outside it had no
 * standard in the legacy app either. */
export function releaserRequirement(
  clusters: number,
  heads: number,
): { minSpeed: number; power: number } | null {
  const row = (releaserJson as ReleaserRow[]).find(
    (r) => Number(r.clusters) === clusters && Number(r.heads) === heads,
  );
  if (!row) return null;
  const minSpeed = Number(row.minSpeed);
  const power = Number(row.power);
  if (!Number.isFinite(minSpeed) || !Number.isFinite(power)) return null;
  return { minSpeed, power };
}
