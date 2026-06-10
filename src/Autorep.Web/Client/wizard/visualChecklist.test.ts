import { describe, it, expect } from "vitest";
import {
  PRE_START_VACUUM_PUMP,
  RUNNING_SECTIONS,
  applyCheckAll,
  checklistComplete,
  preStartSections,
  runningSectionsFor,
} from "./visualChecklist";
import { FAULT_OBSERVATIONS } from "../reference/lookups";
import type { VisualFaultEntry } from "./types";

describe("visual checklist", () => {
  it("includes the releaser-pump section only when present", () => {
    expect(preStartSections(false).map((s) => s.key)).toEqual(["VacuumPump"]);
    expect(preStartSections(true).map((s) => s.key)).toEqual([
      "VacuumPump",
      "ReleaserBeltDriven",
      "ReleaserType",
      "Releasers",
    ]);
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

  it("every check's lookup category resolves to a non-empty standard-fault list", () => {
    const sections = [...preStartSections(true), ...Object.values(RUNNING_SECTIONS)];
    const unresolved = sections
      .flatMap((sec) => sec.items)
      .filter((it) => it.lookup && !(FAULT_OBSERVATIONS[it.lookup]?.length))
      .map((it) => `${it.key} -> ${it.lookup}`);
    expect(unresolved).toEqual([]);
  });
});
