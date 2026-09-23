import { describe, it, expect } from "vitest";
import { isBlankPumpRow, isBlankRegulatorRow, regulatorRows, releaserPumpRows, vacuumPumpRows, withRow } from "./pumpRows";
import { defaultMachineConfiguration, type MachineConfiguration, type VacuumPumpDetail } from "./types";

const cfg = (over: Partial<MachineConfiguration> = {}): MachineConfiguration => ({
  ...defaultMachineConfiguration(),
  ...over,
});

describe("vacuumPumpRows", () => {
  it("shows one row per pump", () => {
    expect(vacuumPumpRows(cfg({ numberOfVacuumPumps: 3 }))).toEqual([{}, {}, {}]);
  });

  it("always shows a row, even with the count box empty", () => {
    expect(vacuumPumpRows(cfg({ numberOfVacuumPumps: 0 }))).toHaveLength(1);
  });

  it("keeps the row count whole, matching the pump reading sets", () => {
    // The count is a plain number box; 2.5 yields two sets of 8a–8c readings, so two rows.
    expect(vacuumPumpRows(cfg({ numberOfVacuumPumps: 2.5 }))).toHaveLength(2);
  });

  it("keeps the rows past the count rather than dropping them", () => {
    // Clearing the count box to retype it passes through 0; the details must survive that.
    const stored = [{ make: "MASPORT" }, { make: "De Laval" }, { make: "GEA" }];
    const midEdit = cfg({ numberOfVacuumPumps: 0, vacuumPumps: stored });
    expect(vacuumPumpRows(midEdit)).toEqual([{ make: "MASPORT" }]);
    expect(vacuumPumpRows({ ...midEdit, numberOfVacuumPumps: 3 })).toEqual(stored);
  });

  it("reads a test captured before the field existed as blank rows", () => {
    const old = cfg({ numberOfVacuumPumps: 2 });
    delete (old as { vacuumPumps?: unknown }).vacuumPumps;
    expect(vacuumPumpRows(old)).toEqual([{}, {}]);
  });
});

describe("releaserPumpRows", () => {
  it("lists nothing until a releaser pump is fitted", () => {
    expect(releaserPumpRows(cfg({ hasReleaserPump: false, releaserPumps: [{ make: "READ" }] }))).toEqual([]);
  });

  it("offers one blank row once fitted, and keeps every row added after that", () => {
    expect(releaserPumpRows(cfg({ hasReleaserPump: true }))).toEqual([{}]);
    expect(
      releaserPumpRows(cfg({ hasReleaserPump: true, releaserPumps: [{ make: "READ" }, { make: "GEA" }] })),
    ).toHaveLength(2);
  });
});

describe("withRow", () => {
  it("patches one row and leaves its siblings alone", () => {
    const rows: VacuumPumpDetail[] = [{ make: "A" }, { make: "B" }];
    expect(withRow(rows, 1, { model: "X" })).toEqual([{ make: "A" }, { make: "B", model: "X" }]);
  });

  it("pads up to the row being edited, so pump 3 can be filled in before pump 2", () => {
    expect(withRow(undefined, 2, { make: "GEA" })).toEqual([{}, {}, { make: "GEA" }]);
  });

  it("never mutates the stored array", () => {
    const rows = [{ make: "A" }];
    withRow(rows, 0, { make: "B" });
    expect(rows).toEqual([{ make: "A" }]);
  });
});

describe("isBlankPumpRow", () => {
  it("is true until something is actually entered", () => {
    expect(isBlankPumpRow({})).toBe(true);
    expect(isBlankPumpRow({ make: "   ", model: null, drivesMilkPump: false })).toBe(true);
    expect(isBlankPumpRow({ motorSize: "7.5" })).toBe(false);
    expect(isBlankPumpRow({ drivesMilkPump: true })).toBe(false);
    // The retired per-pump regulator type prints with the regulators, not as a pump row.
    expect(isBlankPumpRow({ regulatorType: "Servo" })).toBe(true);
  });
});

describe("regulatorRows", () => {
  it("shows one blank line when nothing has been entered", () => {
    expect(regulatorRows(cfg())).toEqual([{}]);
  });

  it("reads the stored lines", () => {
    const regulators = [{ type: "Servo", quantity: 2 }, { type: "Weighted valve", quantity: 1 }];
    expect(regulatorRows(cfg({ regulators }))).toEqual(regulators);
  });

  it("reads an older test's per-pump regulator types as one line per type, counted across the shown pumps", () => {
    const old = cfg({
      numberOfVacuumPumps: 3,
      vacuumPumps: [{ regulatorType: "Servo" }, { regulatorType: " Servo " }, { regulatorType: "Dead weight" }, { regulatorType: "Parked" }],
    });
    delete (old as { regulators?: unknown }).regulators;
    expect(regulatorRows(old)).toEqual([{ type: "Servo", quantity: 2 }, { type: "Dead weight", quantity: 1 }]);
  });

  it("prefers the list once it has been written, even over per-pump types", () => {
    const c = cfg({ vacuumPumps: [{ regulatorType: "Servo" }], regulators: [{ type: "Sentinel", quantity: 1 }] });
    expect(regulatorRows(c)).toEqual([{ type: "Sentinel", quantity: 1 }]);
  });
});

describe("isBlankRegulatorRow", () => {
  it("is true until a type or quantity is entered", () => {
    expect(isBlankRegulatorRow({})).toBe(true);
    expect(isBlankRegulatorRow({ type: "  ", quantity: null })).toBe(true);
    expect(isBlankRegulatorRow({ quantity: 2 })).toBe(false);
    expect(isBlankRegulatorRow({ type: "Servo" })).toBe(false);
  });
});
