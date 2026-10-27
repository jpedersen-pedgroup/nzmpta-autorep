// Which OEM spec a test is judged against. Everything that judges a test from its configuration -
// the pulsator band verdicts, the OEM capacity beside 8a - reads its specs through here.
//
// - A signed-off test carries the specs it was judged against (config.oemSpecs, stamped at sign-off
//   by stampOemSpecs), the way 2g/2h are stored readings: a later edit on the OEM Specs pages
//   doesn't change a finished test. A pin applies only while the test still records that model, so
//   an amendment keeps the original's specs unless the tester changes the equipment.
// - Otherwise the spec in effect on the day the test was started (config.oemSpecs.asOf, set when the
//   test is created): each spec change on the OEM Specs pages applies from a date, so a test never
//   changes spec part-way, and devices switch over on the day. The spec is found in the synced OEM
//   Specs by the item id the test recorded when the tester picked the model (so renaming the item
//   can't lose it), else by name, else it is the bundled legacy spec for that name (standardsData.ts).
// - A test from before dated specs (Oct 2026) has no date: it is judged against the spec in effect
//   from the start - the legacy spec it was judged against then - so its verdicts stay as they were.
//   A change can't be dated before the day it is entered, so nothing entered later reaches it.
import type { MachineConfiguration, OemSpecSnapshot, PinnedSpec } from "../wizard/types";
import { vacuumPumpRows } from "../wizard/pumpRows";
import { nzDate } from "../wizard/nextTestDate";
import { offeredItem } from "./catalogOverrides";
import {
  hasPulsatorBand,
  hasPumpCurve,
  pulsatorBandFor,
  vacuumPumpFor,
  type PulsatorBand,
  type PulsatorSpec,
  type VacuumPumpModel,
  type VacuumPumpSpec,
} from "./standardsData";

interface Selection {
  equipmentId?: string | null;
  brand?: string | null;
  name?: string | null;
}

const loose = (s: string | null | undefined) => (s ?? "").replace(/\s+/g, " ").trim().toLowerCase();

/** The date a test's unpinned specs are taken as of; null - the spec from the start - for a test from
 * before dated specs. */
const asOf = (config: MachineConfiguration): string | null => config.oemSpecs?.asOf ?? null;

/** The OEM Specs bookkeeping a new test starts with: the day it was started, which picks the spec
 * versions it is judged against. */
export function startOemSpecs(createdAt: string): OemSpecSnapshot {
  return { asOf: nzDate(createdAt) };
}

/** Whether a pin is for the equipment the test records now: the same item when both carry an id,
 * else the same name (and brand, where both have one). */
function pinApplies<T>(pin: PinnedSpec<T> | null | undefined, sel: Selection): pin is PinnedSpec<T> {
  if (!pin || !sel.name) return false;
  if (pin.equipmentId && sel.equipmentId) return pin.equipmentId === sel.equipmentId;
  if (loose(pin.name) !== loose(sel.name)) return false;
  return !pin.brand || !sel.brand || loose(pin.brand) === loose(sel.brand);
}

const pulsatorSelection = (config: MachineConfiguration): Selection => ({
  equipmentId: config.pulsatorEquipmentId,
  brand: config.pulsatorBrand,
  name: config.pulsatorModel,
});

function pumpSelection(config: MachineConfiguration, index: number): Selection | null {
  const row = vacuumPumpRows(config)[index];
  return row?.make && row.model ? { equipmentId: row.equipmentId, brand: row.make, name: row.model } : null;
}

/** The pulsator band this test is judged against, or null when the model is unset or has no band. */
export function pulsatorSpecFor(config: MachineConfiguration): PulsatorBand | null {
  const sel = pulsatorSelection(config);
  if (!sel.name) return null;
  const pin = config.oemSpecs?.pulsator;
  if (pinApplies(pin, sel)) {
    return pin.spec && hasPulsatorBand(pin.spec) ? { name: pin.name, brand: pin.brand ?? "", ...pin.spec } : null;
  }
  return pulsatorBandFor(sel.name, sel.brand, sel.equipmentId, asOf(config));
}

/** The OEM curve for the test's pump at `index` (0-based, in pump-row order), or null when the pump
 * has no make and model or its spec has no airflow figure. */
export function vacuumPumpSpecFor(config: MachineConfiguration, index: number): VacuumPumpModel | null {
  const sel = pumpSelection(config, index);
  if (!sel?.name || !sel.brand) return null;
  const pin = config.oemSpecs?.vacuumPumps?.[index];
  if (pinApplies(pin, sel)) {
    return pin.spec && hasPumpCurve(pin.spec) ? { make: pin.brand ?? sel.brand, model: pin.name, ...pin.spec } : null;
  }
  return vacuumPumpFor(sel.brand, sel.name, sel.equipmentId, asOf(config));
}

function pinned<T>(sel: Selection, spec: T | null): PinnedSpec<T> {
  return { equipmentId: sel.equipmentId ?? null, brand: sel.brand ?? null, name: sel.name ?? "", spec };
}

function pulsatorValues(band: PulsatorBand | null): PulsatorSpec | null {
  if (!band) return null;
  const { name: _name, brand: _brand, ...spec } = band;
  return spec;
}

function pumpValues(pump: VacuumPumpModel | null): VacuumPumpSpec | null {
  if (!pump) return null;
  const { make: _make, model: _model, ...spec } = pump;
  return spec;
}

/** The specs to freeze into the test at sign-off: each pin that still applies is kept as it is, and
 * the rest are taken as of the test's date. An unchanged amendment so stamps exactly what its
 * original carried. */
export function stampOemSpecs(config: MachineConfiguration): OemSpecSnapshot {
  const prior = config.oemSpecs;
  const puls = pulsatorSelection(config);
  const pulsator = !puls.name
    ? null
    : pinApplies(prior?.pulsator, puls)
      ? prior!.pulsator!
      : pinned(puls, pulsatorValues(pulsatorSpecFor(config)));
  const vacuumPumps = vacuumPumpRows(config).map((_, i) => {
    const sel = pumpSelection(config, i);
    if (!sel) return null;
    const kept = prior?.vacuumPumps?.[i];
    return pinApplies(kept, sel) ? kept : pinned(sel, pumpValues(vacuumPumpSpecFor(config, i)));
  });
  return { ...(prior?.asOf !== undefined ? { asOf: prior.asOf } : {}), pulsator, vacuumPumps };
}

/** A configuration the server rebuilt from its own columns, with the OEM Specs bookkeeping put back
 * from the test's payload: the columns hold the tester's answers only, and without the stamped specs
 * an administrator would see a finished test judged against today's specs. Pump ids aren't put
 * back; a pin also matches by make and model. */
export function withOemSpecsFrom(
  payload: MachineConfiguration | null | undefined,
  columns: MachineConfiguration,
): MachineConfiguration {
  const out = { ...columns };
  if (payload?.pulsatorEquipmentId) out.pulsatorEquipmentId = payload.pulsatorEquipmentId;
  if (payload?.oemSpecs) out.oemSpecs = payload.oemSpecs;
  return out;
}

/** The id to record for a pulsator model the tester picked, or null when it was typed by hand. */
export function pulsatorEquipmentId(brand: string | null | undefined, model: string | null | undefined): string | null {
  return offeredItem("Pulsator", brand, model)?.id ?? null;
}

/** The id to record for a vacuum pump the tester picked, or null when it was typed by hand. */
export function vacuumPumpEquipmentId(make: string | null | undefined, model: string | null | undefined): string | null {
  return offeredItem("VacuumPump", make, model)?.id ?? null;
}
