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
  });

  it("computes rate/ratio spread and passes within tolerance", () => {
    const rows = [
      row("1", { rate: "60", ratioFront: "62", ratioBack: "60" }),
      row("2", { rate: "63", ratioFront: "64", ratioBack: "61" }),
    ];
    const s = pulsatorSummary(rows);
    expect(s.slowestRate).toBe(60);
    expect(s.fastestRate).toBe(63);
    expect(s.rateSpread).toBe(3);
    expect(s.rateSpreadOk).toBe(true);
    expect(s.lowestRatio).toBe(60);
    expect(s.highestRatio).toBe(64);
    expect(s.ratioSpread).toBe(4);
    expect(s.ratioSpreadOk).toBe(true);
  });

  it("fails when the rate spread exceeds 6 ppm", () => {
    const rows = [row("1", { rate: "55" }), row("2", { rate: "63" })];
    const s = pulsatorSummary(rows);
    expect(s.rateSpread).toBe(8);
    expect(s.rateSpreadOk).toBe(false);
  });
});
