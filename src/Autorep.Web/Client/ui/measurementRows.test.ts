import { describe, it, expect } from "vitest";
import { isRecordedRow, recordedRows } from "./measurementRows";

describe("recordedRows", () => {
  it("drops a row that was added and never filled in", () => {
    expect(isRecordedRow({ id: "a", unit: "", values: {} })).toBe(false);
    expect(isRecordedRow({ id: "b", unit: " ", values: { rate: "" } })).toBe(false);
  });

  it("keeps a row with a unit number or any reading", () => {
    expect(isRecordedRow({ id: "a", unit: "27", values: {} })).toBe(true);
    expect(isRecordedRow({ id: "b", unit: "", values: { rate: "58" } })).toBe(true);
    expect(recordedRows([{ id: "a", unit: "", values: {} }, { id: "b", unit: "3", values: {} }]).map((r) => r.id)).toEqual(["b"]);
    expect(recordedRows(undefined)).toEqual([]);
  });
});
