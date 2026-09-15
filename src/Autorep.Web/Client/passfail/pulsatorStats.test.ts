import { describe, it, expect } from "vitest";
import { pulsatorSummary } from "./pulsatorStats";
import type { MeasurementRow } from "../db/testStore";

function row(unit: string, values: Record<string, string>): MeasurementRow {
  return { id: unit, unit, values };
}

describe("pulsatorSummary", () => {
  it("returns nulls for no rows", () => {
    const s = pulsatorSummary([]);
    expect(s.fastestRate).toBeNull();
    expect(s.highestRatio).toBeNull();
    expect(s.limpOk).toBeNull();
  });

  it("reports the extremes across the recorded rows, front and back ratios together", () => {
    const rows = [
      row("1", { rate: "60", ratioFront: "62", ratioBack: "60" }),
      row("2", { rate: "63", ratioFront: "64", ratioBack: "61" }),
    ];
    const s = pulsatorSummary(rows);
    expect(s.slowestRate).toBe(60);
    expect(s.fastestRate).toBe(63);
    expect(s.lowestRatio).toBe(60);
    expect(s.highestRatio).toBe(64);
    // No spread verdict from the rows: they are the failed units only (see puls.rateSpread).
    expect(s).not.toHaveProperty("rateSpreadOk");
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
    // 64–67 ppm is only 3 apart but entirely above the 59–61 band.
    const rows = [row("1", { rate: "64" }), row("2", { rate: "67" })];
    expect(pulsatorSummary(rows, MODEL).rateBandOk).toBe(false);
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
