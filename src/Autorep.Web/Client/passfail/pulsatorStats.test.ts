import { describe, it, expect } from "vitest";
import { pulsatorSummary } from "./pulsatorStats";
import type { MeasurementRow } from "../db/testStore";

function row(unit: string, values: Record<string, string>): MeasurementRow {
  return { id: unit, unit, values };
}

describe("pulsatorSummary", () => {
  it("returns nulls for no rows", () => {
    const s = pulsatorSummary([]);
    expect(s.rateSpread).toBeNull();
    expect(s.rateSpreadOk).toBeNull();
    expect(s.limpOk).toBeNull();
  });

  it("computes rate spread and per-group ratio spread within tolerance", () => {
    const rows = [
      row("1", { rate: "60", ratioFront: "62", ratioBack: "60" }),
      row("2", { rate: "63", ratioFront: "64", ratioBack: "61" }),
    ];
    const s = pulsatorSummary(rows);
    expect(s.slowestRate).toBe(60);
    expect(s.fastestRate).toBe(63);
    expect(s.rateSpread).toBe(3);
    expect(s.rateSpreadOk).toBe(true);
    // Fronts spread 2 (62–64), backs spread 1 (60–61) → worst group spread 2 (not pooled 4).
    expect(s.ratioSpread).toBe(2);
    expect(s.ratioSpreadOk).toBe(true);
    expect(s.lowestRatio).toBe(60);
    expect(s.highestRatio).toBe(64);
  });

  it("does not fail a designed front/back ratio difference (groups not pooled)", () => {
    // Front quarters run 65, backs run 58 by design — between-pulsator spread is 0 in each group.
    const rows = [
      row("1", { rate: "60", ratioFront: "65", ratioBack: "58" }),
      row("2", { rate: "60", ratioFront: "65", ratioBack: "58" }),
    ];
    const s = pulsatorSummary(rows);
    expect(s.ratioSpread).toBe(0);
    expect(s.ratioSpreadOk).toBe(true);
  });

  it("fails when the rate spread exceeds 6 ppm", () => {
    const rows = [row("1", { rate: "55" }), row("2", { rate: "63" })];
    const s = pulsatorSummary(rows);
    expect(s.rateSpread).toBe(8);
    expect(s.rateSpreadOk).toBe(false);
  });

  it("flags limping over 5% from the per-row limp values", () => {
    const rows = [row("1", { limp: "3" }), row("2", { limp: "7" })];
    const s = pulsatorSummary(rows);
    expect(s.worstLimp).toBe(7);
    expect(s.limpOk).toBe(false);
  });
});

describe("per-model pulsation bands (legacy Pulsator catalogue)", () => {
  // Electronic (Simultaneous) / Dairymaster: rate 59–61 ppm, ratio 59–69%.
  const MODEL = "Electronic (Simultaneous)";

  it("passes when every pulsator runs inside the model band", () => {
    const rows = [
      row("1", { rate: "59", ratioFront: "60", ratioBack: "62" }),
      row("2", { rate: "61", ratioFront: "65", ratioBack: "69" }),
    ];
    const s = pulsatorSummary(rows, MODEL);
    expect(s.rateBand).toEqual({ min: 59, max: 61 });
    expect(s.ratioBand).toEqual({ min: 59, max: 69 });
    expect(s.rateBandOk).toBe(true);
    expect(s.ratioBandOk).toBe(true);
  });

  it("fails the rate band when one pulsator runs outside it, even with a tight spread", () => {
    // Spread 3 ≤ 6 passes the spread check; 64–67 ppm is entirely above the 59–61 band.
    const rows = [row("1", { rate: "64" }), row("2", { rate: "67" })];
    const s = pulsatorSummary(rows, MODEL);
    expect(s.rateSpreadOk).toBe(true);
    expect(s.rateBandOk).toBe(false);
  });

  it("fails the ratio band on a low outlier", () => {
    const rows = [row("1", { ratioFront: "58", ratioBack: "60" })];
    expect(pulsatorSummary(rows, MODEL).ratioBandOk).toBe(false);
  });

  it("joins model names with legacy double-spacing collapsed", () => {
    const rows = [row("1", { rate: "60" })];
    expect(pulsatorSummary(rows, "Electronic  (Simultaneous)").rateBand).toEqual({ min: 59, max: 61 });
  });

  it("reports no band for an unknown or unset model", () => {
    const rows = [row("1", { rate: "60" })];
    for (const model of [undefined, null, "", "No Such Pulsator"]) {
      const s = pulsatorSummary(rows, model);
      expect(s.rateBand).toBeNull();
      expect(s.rateBandOk).toBeNull();
    }
  });

  it("keeps band verdicts null when rows lack the readings", () => {
    const s = pulsatorSummary([row("1", { limp: "2" })], MODEL);
    expect(s.rateBandOk).toBeNull();
    expect(s.ratioBandOk).toBeNull();
  });
});
