import { describe, it, expect } from "vitest";
import { buildAmendmentRecord, computeChanges } from "./amendments";
import { defaultMachineConfiguration } from "../wizard/types";
import type { LocalTest } from "../db/testStore";

function baseTest(): LocalTest {
  const now = "2026-06-11T00:00:00.000Z";
  return {
    id: "v1",
    farmName: "Sunny Acres",
    config: { ...defaultMachineConfiguration(), clusterCount: 20, pulsatorCount: 10 },
    currentStep: "ReviewSignOff",
    visualFaults: {
      "vp.wick": { status: "fault", severity: "Minor", observation: "Oil Wicks Dirty" },
      "vp.oilWater": { status: "ok" },
    },
    attestations: [],
    readings: { "tr.workingVacuum": 48, "tr.airlineDropRR": 2 },
    recommendations: { "vp.wick": "Clean the wicks" },
    dataFields: {},
    pulsatorRows: [
      { id: "p1", unit: "1", values: { rate: "60", ratioFront: "62" } },
      { id: "p2", unit: "2", values: { rate: "58" } },
    ],
    notes: "Original comment",
    createdAt: now,
    updatedAt: now,
    markedCompleteAt: now,
    syncState: "uploaded",
    version: 1,
  };
}

function editedCopy(patch: Partial<LocalTest>): LocalTest {
  return { ...baseTest(), id: "v2", version: 2, supersedesId: "v1", ...patch };
}

describe("computeChanges", () => {
  it("returns no changes for an untouched copy", () => {
    expect(computeChanges(baseTest(), editedCopy({}))).toEqual([]);
  });

  it("records config changes with friendly labels and Yes/No booleans", () => {
    const changes = computeChanges(
      baseTest(),
      editedCopy({ config: { ...baseTest().config, clusterCount: 24, hasAcr: true } }),
    );
    expect(changes).toContainEqual({ section: "Machine configuration", label: "Clusters", from: "20", to: "24" });
    expect(changes).toContainEqual({ section: "Machine configuration", label: "ACRs", from: "No", to: "Yes" });
  });

  it("records reading changes with the reading's label and unit, including cleared values", () => {
    const changes = computeChanges(
      baseTest(),
      editedCopy({ readings: { "tr.workingVacuum": 50 } }), // airlineDropRR removed
    );
    const vac = changes.find((c) => c.label.toLowerCase().includes("working vacuum"));
    expect(vac).toBeDefined();
    expect(vac!.section).toBe("Numerical readings");
    expect(vac!.from).toMatch(/^48/);
    expect(vac!.to).toMatch(/^50/);
    const dropped = changes.find((c) => c.from.startsWith("2") && c.to === "—");
    expect(dropped).toBeDefined();
  });

  it("records per-unit row edits, additions and removals", () => {
    const changes = computeChanges(
      baseTest(),
      editedCopy({
        pulsatorRows: [
          { id: "p1", unit: "1", values: { rate: "61", ratioFront: "62" } }, // edited
          { id: "p3", unit: "3", values: { rate: "59" } }, // added — p2 removed
        ],
      }),
    );
    expect(changes).toContainEqual({
      section: "Pulsator test results", label: "Pulsator 1 · Rate (ppm)", from: "60", to: "61",
    });
    expect(changes).toContainEqual({
      section: "Pulsator test results", label: "Pulsator 2", from: "Rate (ppm) 58", to: "Removed",
    });
    expect(changes).toContainEqual({
      section: "Pulsator test results", label: "Pulsator 3", from: "—", to: "Added (Rate (ppm) 59)",
    });
  });

  it("records a note edit even when the observation is unchanged", () => {
    const changes = computeChanges(
      editedCopy({
        visualFaults: { "vp.wick": { status: "fault", severity: "Minor", observation: "Oil Wicks Dirty", note: "rear pump" } },
      }),
      editedCopy({
        visualFaults: { "vp.wick": { status: "fault", severity: "Minor", observation: "Oil Wicks Dirty", note: "front pump" } },
      }),
    );
    expect(changes).toHaveLength(1);
    expect(changes[0].from).toContain("note: rear pump");
    expect(changes[0].to).toContain("note: front pump");
  });

  it("records replacing the analyser attachment with a same-named file", () => {
    const changes = computeChanges(
      editedCopy({ pulsationPdf: { name: "export.pdf", base64: "AA==", size: 104857, attachedAt: "2026-06-10T09:00:00.000Z" } }),
      editedCopy({ pulsationPdf: { name: "export.pdf", base64: "BB==", size: 158223, attachedAt: "2026-07-06T10:30:00.000Z" } }),
    );
    expect(changes).toHaveLength(1);
    expect(changes[0].label).toBe("Pulsation analyser attachment");
    expect(changes[0].from).toContain("102 KB");
    expect(changes[0].to).toContain("155 KB");
  });

  it("does not report absent vs explicit-false guards as a change", () => {
    const base = editedCopy({});
    delete base.guardsOnPulsators;
    expect(computeChanges(base, editedCopy({ guardsOnPulsators: false }))).toEqual([]);
    expect(computeChanges(base, editedCopy({ guardsOnPulsators: true }))).toHaveLength(1);
  });

  it("records visual-check status changes and comment edits", () => {
    const changes = computeChanges(
      baseTest(),
      editedCopy({
        visualFaults: { ...baseTest().visualFaults, "vp.wick": { status: "ok" } },
        notes: "Amended comment",
      }),
    );
    const wick = changes.find((c) => c.section === "Visual checks");
    expect(wick).toBeDefined();
    expect(wick!.from).toContain("Fault — Minor: Oil Wicks Dirty");
    expect(wick!.to).toBe("OK");
    expect(changes).toContainEqual({
      section: "Other", label: "Tester comment", from: "Original comment", to: "Amended comment",
    });
  });

  it("orders changes by report section", () => {
    const changes = computeChanges(
      baseTest(),
      editedCopy({
        notes: "Amended",
        config: { ...baseTest().config, clusterCount: 24 },
        farmName: "Renamed Farm",
      }),
    );
    const sections = changes.map((c) => c.section);
    expect(sections).toEqual(["Farm", "Machine configuration", "Other"]);
  });
});

describe("buildAmendmentRecord", () => {
  it("captures version linkage, the amender and the diff", () => {
    const base = baseTest();
    const edited = editedCopy({ notes: "Amended" });
    const rec = buildAmendmentRecord(base, edited, "2026-07-06T00:00:00.000Z", "tester@local");
    expect(rec.version).toBe(2);
    expect(rec.baseVersion).toBe(1);
    expect(rec.baseCompletedAt).toBe(base.markedCompleteAt);
    expect(rec.amendedBy).toBe("tester@local");
    expect(rec.baseUnavailable).toBeUndefined();
    expect(rec.changes).toHaveLength(1);
  });

  it("omits amendedBy when no identity is available rather than storing a blank", () => {
    const rec = buildAmendmentRecord(baseTest(), editedCopy({}), "2026-07-06T00:00:00.000Z", null);
    expect("amendedBy" in rec).toBe(false);
  });

  it("flags a missing base instead of silently recording nothing", () => {
    const rec = buildAmendmentRecord(undefined, editedCopy({}), "2026-07-06T00:00:00.000Z");
    expect(rec.baseUnavailable).toBe(true);
    expect(rec.baseVersion).toBe(1);
    expect(rec.changes).toEqual([]);
  });
});
