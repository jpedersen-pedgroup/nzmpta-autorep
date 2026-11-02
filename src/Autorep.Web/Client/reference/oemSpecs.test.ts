import { describe, it, expect, afterEach } from "vitest";
import { applyEquipmentOverrides, clearEquipmentOverrides, specAsOf, type EquipmentDto } from "./catalogOverrides";
import {
  pulsatorEquipmentId,
  startOemSpecs,
  pulsatorSpecFor,
  stampOemSpecs,
  vacuumPumpEquipmentId,
  vacuumPumpSpecFor,
  withOemSpecsFrom,
} from "./oemSpecs";
import { pulsatorSummary } from "../passfail/pulsatorStats";
import { deriveReadings } from "../passfail/derived";
import { testRecordSections } from "../passfail/standards";
import { pulsatorModelOptionsForBrand } from "./lookups";
import { defaultMachineConfiguration, type MachineConfiguration } from "../wizard/types";
import type { MeasurementRow } from "../db/testStore";

afterEach(() => clearEquipmentOverrides());

const cfg = (over: Partial<MachineConfiguration> = {}): MachineConfiguration => ({
  ...defaultMachineConfiguration(),
  clusterCount: 20,
  ...over,
});

const rows = (...rates: number[]): MeasurementRow[] =>
  rates.map((rate, i) => ({ id: String(i), unit: String(i + 1), values: { rate: String(rate), ratioFront: "62", ratioBack: "63" } }));

// The bundled legacy band for this model is rate 59–61 ppm, ratio 59–69%.
const BUNDLED = "Electronic (Simultaneous)";

const pulsator = (over: Partial<EquipmentDto>): EquipmentDto => ({
  id: "p-1",
  type: "Pulsator",
  name: "NewCo P1",
  brand: "NewCo",
  spec: { rateMin: 58, rateMax: 62, ratioMin: 60, ratioMax: 70 },
  ...over,
});

describe("current OEM specs (nothing stamped)", () => {
  it("uses the bundled legacy spec before the first sync", () => {
    expect(pulsatorSpecFor(cfg({ pulsatorBrand: "Dairymaster", pulsatorModel: BUNDLED }))).toMatchObject({ rateMin: 59, rateMax: 61 });
  });

  it("gives a model added on the OEM Specs pages a verdict once it has synced", () => {
    applyEquipmentOverrides([pulsator({})]);
    const config = cfg({ pulsatorBrand: "NewCo", pulsatorModel: "NewCo P1", pulsatorEquipmentId: pulsatorEquipmentId("NewCo", "NewCo P1") });
    expect(config.pulsatorEquipmentId).toBe("p-1");
    const inside = pulsatorSummary(rows(58, 62), config);
    expect(inside.rateBand).toEqual({ min: 58, max: 62 });
    expect(inside.rateBandOk).toBe(true);
    expect(pulsatorSummary(rows(58, 63), config).rateBandOk).toBe(false);
  });

  it("follows a renamed item by the id the test recorded", () => {
    // The tester picked "NewCo P1"; the SuperAdmin has since renamed it.
    applyEquipmentOverrides([pulsator({ name: "NewCo P1 Mk II" })]);
    const config = cfg({ pulsatorBrand: "NewCo", pulsatorModel: "NewCo P1", pulsatorEquipmentId: "p-1" });
    expect(pulsatorSpecFor(config)).toMatchObject({ rateMin: 58, rateMax: 62 });
    // Without the id (a test from before ids were recorded) the old name finds nothing.
    expect(pulsatorSpecFor({ ...config, pulsatorEquipmentId: null })).toBeNull();
  });

  it("prefers the synced spec to the bundled one, and an edited spec to the old one", () => {
    applyEquipmentOverrides([pulsator({ id: "p-9", name: BUNDLED, brand: "Dairymaster", spec: { rateMin: 55, rateMax: 65 } })]);
    const band = pulsatorSpecFor(cfg({ pulsatorBrand: "Dairymaster", pulsatorModel: BUNDLED }));
    expect(band).toMatchObject({ rateMin: 55, rateMax: 65, ratioMin: null });
    expect(pulsatorSummary(rows(60), cfg({ pulsatorBrand: "Dairymaster", pulsatorModel: BUNDLED })).ratioBand).toBeNull();
  });

  it("gives no verdict for a synced model with no spec, rather than falling back to the bundled one", () => {
    applyEquipmentOverrides([pulsator({ name: BUNDLED, brand: "Dairymaster", spec: null })]);
    expect(pulsatorSpecFor(cfg({ pulsatorBrand: "Dairymaster", pulsatorModel: BUNDLED }))).toBeNull();
  });

  it("falls back to the bundled spec for a recorded model the synced set no longer has", () => {
    applyEquipmentOverrides([pulsator({})]);
    expect(pulsatorSpecFor(cfg({ pulsatorBrand: "Dairymaster", pulsatorModel: BUNDLED }))).toMatchObject({ rateMin: 59 });
  });

  it("keeps the bundled specs while the cached set predates specs", () => {
    // A device's cache from before OEM spec editing: no `spec` on any row.
    applyEquipmentOverrides([{ type: "Pulsator", name: BUNDLED, brand: "Dairymaster" }]);
    expect(pulsatorSpecFor(cfg({ pulsatorBrand: "Dairymaster", pulsatorModel: BUNDLED }))).toMatchObject({ rateMin: 59 });
  });

  it("still judges a model that has been deactivated, but no longer offers it", () => {
    applyEquipmentOverrides([], [pulsator({})]);
    expect(pulsatorModelOptionsForBrand("NewCo")).toEqual([]);
    expect(pulsatorEquipmentId("NewCo", "NewCo P1")).toBeNull();
    expect(pulsatorSpecFor(cfg({ pulsatorBrand: "NewCo", pulsatorModel: "NewCo P1", pulsatorEquipmentId: "p-1" }))).toMatchObject({
      rateMin: 58,
    });
  });

  it("records no id for a model typed by hand", () => {
    applyEquipmentOverrides([pulsator({})]);
    expect(pulsatorEquipmentId("NewCo", "Something else")).toBeNull();
    expect(pulsatorEquipmentId("Other brand", "NewCo P1")).toBeNull();
    expect(pulsatorEquipmentId(null, "newco  p1")).toBe("p-1");
  });

  it("reads a vacuum pump's synced spec by id, then by make and model", () => {
    applyEquipmentOverrides([
      { id: "v-1", type: "VacuumPump", name: "DVP1600 Mk II", brand: "De Laval", spec: { airFlow: 1.2, minRpm: 300, maxRpm: 1500 } },
    ]);
    expect(vacuumPumpEquipmentId("de laval", "DVP1600 MK II")).toBe("v-1");
    const byId = cfg({ vacuumPumps: [{ make: "De Laval", model: "DVP1600", equipmentId: "v-1" }] });
    expect(vacuumPumpSpecFor(byId, 0)).toMatchObject({ airFlow: 1.2, model: "DVP1600 Mk II" });
    // The old name without an id: not in the synced set, so the bundled legacy curve.
    expect(vacuumPumpSpecFor(cfg({ vacuumPumps: [{ make: "De Laval", model: "DVP1600" }] }), 0)).toMatchObject({ airFlow: 1.15 });
  });
});

describe("the OEM specs a test was signed off against", () => {
  it("keeps an old test's verdict when the spec is edited afterwards", () => {
    const signedOff = cfg({ pulsatorBrand: "NewCo", pulsatorModel: "NewCo P1", pulsatorEquipmentId: "p-1" });
    applyEquipmentOverrides([pulsator({})]);
    const stamped = { ...signedOff, oemSpecs: stampOemSpecs(signedOff) };
    expect(stamped.oemSpecs.pulsator).toMatchObject({ equipmentId: "p-1", name: "NewCo P1", spec: { rateMin: 58, rateMax: 62 } });

    // Maria widens the band later; the finished test is still judged against 58–62.
    applyEquipmentOverrides([pulsator({ spec: { rateMin: 50, rateMax: 70, ratioMin: 60, ratioMax: 70 } })]);
    expect(pulsatorSummary(rows(63), stamped).rateBandOk).toBe(false);
    expect(pulsatorSummary(rows(63), signedOff).rateBandOk).toBe(true);
  });

  it("gives no verdict to a test signed off before its model had a spec", () => {
    applyEquipmentOverrides([pulsator({ spec: null })]);
    const config = cfg({ pulsatorBrand: "NewCo", pulsatorModel: "NewCo P1", pulsatorEquipmentId: "p-1" });
    const stamped = { ...config, oemSpecs: stampOemSpecs(config) };
    expect(stamped.oemSpecs.pulsator?.spec).toBeNull();
    applyEquipmentOverrides([pulsator({})]);
    expect(pulsatorSpecFor(stamped)).toBeNull();
  });

  it("stops applying once the test records different equipment", () => {
    applyEquipmentOverrides([pulsator({}), pulsator({ id: "p-2", name: "NewCo P2", spec: { rateMin: 40, rateMax: 50 } })]);
    const original = cfg({ pulsatorBrand: "NewCo", pulsatorModel: "NewCo P1", pulsatorEquipmentId: "p-1" });
    const stamped = { ...original, oemSpecs: stampOemSpecs(original) };
    // An amendment that changes the model is judged against the new model's spec...
    const amended = { ...stamped, pulsatorModel: "NewCo P2", pulsatorEquipmentId: "p-2" };
    expect(pulsatorSpecFor(amended)).toMatchObject({ rateMin: 40 });
    // ...and its sign-off pins that one.
    expect(stampOemSpecs(amended).pulsator).toMatchObject({ equipmentId: "p-2", spec: { rateMin: 40 } });
  });

  it("stamps an unchanged amendment exactly as its original", () => {
    applyEquipmentOverrides([pulsator({})]);
    const original = cfg({
      pulsatorBrand: "NewCo",
      pulsatorModel: "NewCo P1",
      pulsatorEquipmentId: "p-1",
      vacuumPumps: [{ make: "De Laval", model: "DVP1600" }],
    });
    const first = stampOemSpecs(original);
    applyEquipmentOverrides([pulsator({ spec: { rateMin: 1, rateMax: 2 } })]);
    expect(stampOemSpecs({ ...original, oemSpecs: first })).toEqual(first);
  });

  it("pins a test's pumps by position, with no pin for a blank row", () => {
    const config = cfg({
      numberOfVacuumPumps: 2,
      vacuumPumps: [{ make: "De Laval", model: "DVP1600" }, {}],
    });
    const snap = stampOemSpecs(config);
    expect(snap.pulsator).toBeNull();
    expect(snap.vacuumPumps).toHaveLength(2);
    expect(snap.vacuumPumps?.[0]).toMatchObject({ brand: "De Laval", name: "DVP1600", spec: { airFlow: 1.15, maxRpm: 1400 } });
    expect(snap.vacuumPumps?.[1]).toBeNull();
  });

  it("works a signed-off pump's OEM capacity out from its pinned curve", () => {
    const config = cfg({ vacuumPumps: [{ make: "De Laval", model: "DVP1600" }] });
    const stamped = { ...config, oemSpecs: stampOemSpecs(config) };
    applyEquipmentOverrides([{ id: "v-1", type: "VacuumPump", name: "DVP1600", brand: "De Laval", spec: { airFlow: 2 } }]);
    expect(deriveReadings(stamped, { "tr.pumpMaxSpeed1": 1400 })["tr.pumpOemCapacity1"]).toBe(1610);
    expect(deriveReadings(config, { "tr.pumpMaxSpeed1": 1400 })["tr.pumpOemCapacity1"]).toBe(2800);
    const oem = testRecordSections(stamped).flatMap((s) => s.readings).find((r) => r.key === "tr.pumpOemCapacity1");
    expect(oem?.hint).toMatch(/1\.15 L\/min per rpm/);
  });

  it("survives a configuration the server rebuilt from its columns", () => {
    const payload = cfg({ pulsatorBrand: "NewCo", pulsatorModel: "NewCo P1", pulsatorEquipmentId: "p-1" });
    applyEquipmentOverrides([pulsator({})]);
    const stamped = { ...payload, oemSpecs: stampOemSpecs(payload) };
    const columns = cfg({ pulsatorBrand: "NewCo", pulsatorModel: "NewCo P1" });
    const shown = withOemSpecsFrom(stamped, columns);
    expect(shown.oemSpecs).toEqual(stamped.oemSpecs);
    expect(shown.pulsatorEquipmentId).toBe("p-1");
    expect(withOemSpecsFrom(undefined, columns)).toEqual(columns);
    expect(Object.keys(withOemSpecsFrom(cfg(), columns))).not.toContain("oemSpecs");
  });
});

describe("dated OEM spec versions", () => {
  // The legacy band from the start, and a change Maria has entered to apply from 1 December.
  const dated = (over: Partial<EquipmentDto> = {}): EquipmentDto =>
    pulsator({
      spec: { rateMin: 58, rateMax: 62 },
      specs: [
        { effectiveFrom: null, spec: { rateMin: 58, rateMax: 62 } },
        { effectiveFrom: "2026-12-01", spec: { rateMin: 55, rateMax: 65 } },
      ],
      ...over,
    });
  const p1 = (oemSpecs?: MachineConfiguration["oemSpecs"]) =>
    cfg({ pulsatorBrand: "NewCo", pulsatorModel: "NewCo P1", pulsatorEquipmentId: "p-1", oemSpecs });

  it("dates a new test by the New Zealand day it was started", () => {
    expect(startOemSpecs("2026-11-30T12:00:00.000Z")).toEqual({ asOf: "2026-12-01" }); // 1 am NZDT
    expect(startOemSpecs("2026-11-30T10:00:00.000Z")).toEqual({ asOf: "2026-11-30" });
  });

  it("judges a test against the version in effect on the day it was started", () => {
    applyEquipmentOverrides([dated()]);
    expect(pulsatorSpecFor(p1(startOemSpecs("2026-11-30T10:00:00.000Z")))).toMatchObject({ rateMin: 58, rateMax: 62 });
    expect(pulsatorSpecFor(p1(startOemSpecs("2026-11-30T12:00:00.000Z")))).toMatchObject({ rateMin: 55, rateMax: 65 });
    expect(pulsatorSpecFor(p1({ asOf: "2027-03-01" }))).toMatchObject({ rateMin: 55 });
  });

  it("doesn't change spec part-way through a test when the change comes into effect", () => {
    applyEquipmentOverrides([dated()]);
    const started = p1({ asOf: "2026-11-30" });
    const atSignOff = { ...started, oemSpecs: stampOemSpecs(started) }; // signed off on 2 December
    expect(atSignOff.oemSpecs.asOf).toBe("2026-11-30");
    expect(atSignOff.oemSpecs.pulsator?.spec).toMatchObject({ rateMin: 58, rateMax: 62 });
    expect(pulsatorSummary(rows(64), atSignOff).rateBandOk).toBe(false);
  });

  it("keeps a test from before dated specs on the legacy spec, whatever is entered later", () => {
    // In progress or signed off before this release: no date and no pins.
    applyEquipmentOverrides([dated()]);
    const old = p1(undefined);
    expect(pulsatorSpecFor(old)).toMatchObject({ rateMin: 58, rateMax: 62 });
    expect(pulsatorSummary(rows(64), old).rateBandOk).toBe(false);
    // Signed off now, it pins the legacy spec too.
    expect(stampOemSpecs(old)).toEqual({
      pulsator: { equipmentId: "p-1", brand: "NewCo", name: "NewCo P1", spec: expect.objectContaining({ rateMin: 58 }) },
      vacuumPumps: [null],
    });
  });

  it("gives an older test no verdict for a model whose first spec is dated later", () => {
    // Added on the OEM Specs pages with a spec from 1 December: nothing applied before.
    applyEquipmentOverrides([pulsator({ specs: [{ effectiveFrom: "2026-12-01", spec: { rateMin: 55, rateMax: 65 } }] })]);
    expect(pulsatorSpecFor(p1(undefined))).toBeNull();
    expect(pulsatorSpecFor(p1({ asOf: "2026-11-30" }))).toBeNull();
    expect(pulsatorSpecFor(p1({ asOf: "2026-12-01" }))).toMatchObject({ rateMin: 55 });
  });

  it("reads a version that says the model has no spec from a date", () => {
    applyEquipmentOverrides([dated({ specs: [{ effectiveFrom: null, spec: { rateMin: 58, rateMax: 62 } }, { effectiveFrom: "2026-12-01", spec: null }] })]);
    expect(pulsatorSpecFor(p1({ asOf: "2026-11-30" }))).toMatchObject({ rateMin: 58 });
    expect(pulsatorSpecFor(p1({ asOf: "2026-12-05" }))).toBeNull();
  });

  it("picks the latest version on or before the date, and reads an older cache as one version from the start", () => {
    const item = dated({
      specs: [
        { effectiveFrom: null, spec: { rateMin: 1 } },
        { effectiveFrom: "2026-12-01", spec: { rateMin: 2 } },
        { effectiveFrom: "2027-01-01", spec: { rateMin: 3 } },
      ],
    });
    expect(specAsOf(item, null)).toEqual({ rateMin: 1 });
    expect(specAsOf(item, "2026-11-30")).toEqual({ rateMin: 1 });
    expect(specAsOf(item, "2026-12-01")).toEqual({ rateMin: 2 });
    expect(specAsOf(item, "2027-06-30")).toEqual({ rateMin: 3 });
    // Cached before effective dates: `spec` alone, which was the spec from the start.
    expect(specAsOf({ type: "Pulsator", name: "X", spec: { rateMin: 9 } }, "2027-01-01")).toEqual({ rateMin: 9 });
  });

  it("dates vacuum pump specs the same way", () => {
    applyEquipmentOverrides([
      {
        id: "v-1", type: "VacuumPump", name: "DVP1600", brand: "De Laval",
        specs: [{ effectiveFrom: null, spec: { airFlow: 1.15 } }, { effectiveFrom: "2026-12-01", spec: { airFlow: 1.2 } }],
      },
    ]);
    const pump = (asOf: string) => cfg({ vacuumPumps: [{ make: "De Laval", model: "DVP1600" }], oemSpecs: { asOf } });
    expect(deriveReadings(pump("2026-11-30"), { "tr.pumpMaxSpeed1": 1400 })["tr.pumpOemCapacity1"]).toBe(1610);
    expect(deriveReadings(pump("2026-12-01"), { "tr.pumpMaxSpeed1": 1400 })["tr.pumpOemCapacity1"]).toBe(1680);
  });
});
