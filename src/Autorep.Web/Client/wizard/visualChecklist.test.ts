import { describe, it, expect } from "vitest";
import {
  PRE_START_VACUUM_PUMP,
  applyCheckAll,
  checklistComplete,
  preStartSections,
  runningSectionsFor,
} from "./visualChecklist";
import type { VisualFaultEntry } from "./types";

describe("visual checklist", () => {
  it("includes the releaser-pump section only when present", () => {
    expect(preStartSections(false).map((s) => s.key)).toEqual(["VacuumPump"]);
    expect(preStartSections(true).map((s) => s.key)).toEqual(["VacuumPump", "ReleaserMilkPump"]);
  });

  it("maps resolver section keys to running checklist sections, skipping unknowns", () => {
    expect(runningSectionsFor(["Rotaries", "Inlets"]).map((s) => s.key)).toEqual(["Rotaries", "Inlets"]);
    expect(runningSectionsFor(["Nope"])).toEqual([]);
  });

  it("is complete only when every item has a status", () => {
    const sections = [PRE_START_VACUUM_PUMP];
    expect(checklistComplete(sections, {})).toBe(false);
    expect(checklistComplete(sections, applyCheckAll(sections, {}))).toBe(true);
  });

  it("check-all sets blanks to OK but keeps existing faults", () => {
    const sections = [PRE_START_VACUUM_PUMP];
    const before: Record<string, VisualFaultEntry> = { "vp.wick": { status: "fault", severity: "Major" } };
    const after = applyCheckAll(sections, before);
    expect(after["vp.wick"].status).toBe("fault");
    expect(after["vp.oilWater"].status).toBe("ok");
  });
});
