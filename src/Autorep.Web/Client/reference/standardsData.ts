// Typed access to the reference catalogues extracted from the legacy Autorep database by
// tools/reference-data/extract_legacy_reference.py — see the "Legacy reference-data audit" in
// plans/reference/standards-audit.md. These are the per-model numbers the manual carries only as
// image-only scans; the legacy app judged tests against them (tick/cross columns in
// PulsationSystemResultRange and MMAdditionalTR), so wiring them in restores legacy parity.
import pulsatorBandsJson from "./pulsatorBands.json";
import vacuumPumpsJson from "./vacuumPumps.json";
import milkPumpsJson from "./milkPumps.json";
import releaserJson from "./releaserSpeedPower.json";
import reserveReceiverJson from "./reserveReceiver.json";

/** Per-model pulsation bands (legacy `Pulsator`). Rate in ppm, ratios in %, phases in %/ms. */
export interface PulsatorBand {
  name: string;
  brand: string;
  rateMin: number;
  rateMax: number;
  ratioMin: number;
  ratioMax: number;
  phaseB: number;
  phaseD: number;
  maxChamberVacuum: number;
  recommendedRate: number | null;
  recommendedRatio: number | null;
}

/** OEM vacuum-pump curve rows (legacy `VPModel`), joined to the pump make/model captured in the
 * machine configuration (see vacuumPumpFor). `airFlow` is the curve: L/min of air per rpm at
 * 50 kPa - a De Laval DVP1600 is 1.15 x 1400 rpm = 1610 L/min, its nameplate figure. */
export interface VacuumPumpModel {
  make: string;
  model: string;
  minRpm: number | null;
  maxRpm: number | null;
  airFlow: number | null;
  motorSizeFactor: number | null;
  waterFlowRate: number | null;
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
 * on 6a/6b acceptance limits is unresolved — confirm with NZMPTA before turning it into a rule. */
export interface ReceiverReserveRow {
  milklineDiameter: number;
  workingVacuum: string;
  requiredReserve: number;
}

export const VACUUM_PUMPS: VacuumPumpModel[] = vacuumPumpsJson as VacuumPumpModel[];
export const MILK_PUMPS: MilkPumpModel[] = milkPumpsJson as MilkPumpModel[];
export const RECEIVER_RESERVE: ReceiverReserveRow[] = reserveReceiverJson as ReceiverReserveRow[];

const collapse = (s: string) => s.replace(/\s+/g, " ").trim();

// The catalogue the wizard's model dropdown ships (pulsators.json) and the bands file are both
// extracted from the same legacy table with whitespace collapsed, so names join exactly; the
// normalised map guards against stray spacing in stored tests.
const bandsByName = new Map<string, PulsatorBand>(
  (pulsatorBandsJson as PulsatorBand[]).map((b) => [collapse(b.name), b]),
);

/** The pulsation band for a pulsator model, or null when the model is unknown/unset. Bands with a
 * non-positive max are treated as absent — a handful of legacy rows carry zeros, and a 0–0 band
 * would fail every reading rather than mean "no standard". */
export function pulsatorBandFor(model: string | null | undefined): PulsatorBand | null {
  if (!model) return null;
  const band = bandsByName.get(collapse(model));
  if (!band) return null;
  return band.rateMax > 0 || band.ratioMax > 0 ? band : null;
}

const pumpKey = (make: string, model: string) => `${collapse(make).toLowerCase()}|${collapse(model).toLowerCase()}`;
const pumpsByMakeModel = new Map<string, VacuumPumpModel>(
  VACUUM_PUMPS.map((p) => [pumpKey(p.make, p.model), p]),
);

/** The OEM curve row for a vacuum pump, or null when the make/model isn't in the legacy catalogue
 * (a pump typed in by hand) or the row carries no usable airflow figure. Matched case- and
 * spacing-insensitively: the make may have been typed rather than picked. */
export function vacuumPumpFor(
  make: string | null | undefined,
  model: string | null | undefined,
): VacuumPumpModel | null {
  if (!make || !model) return null;
  const pump = pumpsByMakeModel.get(pumpKey(make, model));
  return pump != null && pump.airFlow != null && pump.airFlow > 0 ? pump : null;
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
