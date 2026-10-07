import { describe, it, expect } from "vitest";
import { buildAdminPayload, draftFromPayload, withinScope } from "./adminEdit";
import { buildAmendmentRecord, computeChanges, computeChangesWithPaths } from "./amendments";
import { defaultMachineConfiguration } from "../wizard/types";
import type { LocalTest } from "../db/testStore";

const SIGNED = "2026-09-01T00:00:00.000Z";

/** Version 2 of a test as the server stores it: the device's LocalTest, with a history. */
function stored(): Record<string, unknown> {
  return {
    id: "client-v2",
    farmName: "Kowhai Flats (as tested)",
    config: { ...defaultMachineConfiguration(), clusterCount: 20, plantType: "Rotary" },
    currentStep: "ReviewSignOff",
    visualFaults: { "vp.wick": { status: "fault", severity: "Minor", observation: "Oil Wicks Dirty" } },
    attestations: [{ step: "ReviewSignOff", attestedAt: SIGNED, text: "signed" }],
    readings: { "tr.workingVacuum": 48 },
    recommendations: { "vp.wick": "Clean the wicks" },
    dataFields: {},
    notes: "Original comment",
    createdAt: SIGNED,
    updatedAt: SIGNED,
    markedCompleteAt: SIGNED,
    syncState: "uploaded",
    readonly: true,
    version: 2,
    supersedesId: "client-v1",
    amendments: [{ version: 2, amendedAt: SIGNED, baseVersion: 1, changes: [] }],
  };
}

/** The read-only view the admin viewer builds: the farm's CURRENT name and the server's config columns. */
function view(): LocalTest {
  return {
    id: "server-row-id",
    farmName: "Kowhai Flats (renamed since)",
    config: { ...defaultMachineConfiguration(), clusterCount: 30, plantType: "Rotary" },
    currentStep: "Setup",
    visualFaults: {},
    attestations: [],
    readings: {},
    recommendations: {},
    dataFields: {},
    createdAt: SIGNED,
    updatedAt: "2026-10-07T00:00:00.000Z",
    markedCompleteAt: SIGNED,
    syncState: "uploaded",
    readonly: true,
    version: 2,
  };
}

describe("an administrator's edit", () => {
  it("starts from the stored record, not the view of it", () => {
    const draft = draftFromPayload(stored(), view());

    expect(draft.id).toBe("server-row-id");
    expect(draft.farmName).toBe("Kowhai Flats (as tested)");
    expect(draft.config.clusterCount).toBe(20);
    expect(draft.recommendations).toEqual({ "vp.wick": "Clean the wicks" });
    expect(draft.readonly).toBe(false);
    expect(draft.version).toBe(2);
    expect(computeChanges(draft, draftFromPayload(stored(), view()))).toEqual([]);
  });

  it("records the admin's changes as an amendment with its reason, in the tester's terms", () => {
    const base = draftFromPayload(stored(), view());
    const edited: LocalTest = { ...base, version: 3, recommendations: { "vp.wick": "Replace the wicks" } };

    const record = { ...buildAmendmentRecord(base, edited, "2026-10-07T01:00:00.000Z", "sam@nzmpta"), reason: "Wording" };

    expect(record).toMatchObject({ version: 3, baseVersion: 2, baseCompletedAt: SIGNED, amendedBy: "sam@nzmpta", reason: "Wording" });
    expect(record.changes).toEqual([
      { section: "Recommendations", label: "Vacuum pumps (remove guard) · Wick condition", from: "Clean the wicks", to: "Replace the wicks" },
    ]);
  });

  it("a Company Administrator's save carries only the recommendations and comments", () => {
    const base = draftFromPayload(stored(), view());
    // Even if the draft somehow held other changes, they don't go into the request.
    const edited: LocalTest = {
      ...base,
      version: 3,
      notes: undefined,
      recommendations: { "vp.wick": "Replace the wicks" },
      readings: { "tr.workingVacuum": 40 },
    };
    const record = buildAmendmentRecord(base, edited, "2026-10-07T01:00:00.000Z");

    const payload = buildAdminPayload(stored(), edited, "summary", record);

    expect(payload.recommendations).toEqual({ "vp.wick": "Replace the wicks" });
    expect("notes" in payload).toBe(false);
    expect(payload.readings).toEqual({ "tr.workingVacuum": 48 });
    expect(payload.farmName).toBe("Kowhai Flats (as tested)");
    expect((payload.amendments as unknown[]).length).toBe(2);
  });

  it("a Super-Administrator's save carries every field the wizard edits", () => {
    const base = draftFromPayload(stored(), view());
    const edited: LocalTest = {
      ...base,
      version: 3,
      readings: { "tr.workingVacuum": 46 },
      config: { ...base.config, clusterCount: 24 },
      nextTestDate: "2027-06-30",
    };

    const payload = buildAdminPayload(stored(), edited, "full", buildAmendmentRecord(base, edited, "2026-10-07T01:00:00.000Z"));

    expect(payload.readings).toEqual({ "tr.workingVacuum": 46 });
    expect((payload.config as { clusterCount: number }).clusterCount).toBe(24);
    expect(payload.nextTestDate).toBe("2027-06-30");
    // What isn't the wizard's to edit stays as stored.
    expect(payload.farmName).toBe("Kowhai Flats (as tested)");
    expect(payload.attestations).toEqual(stored().attestations);
  });

  it("drops a Company Administrator's change to anything but the recommendations and comments as it's made", () => {
    expect(withinScope("summary", { notes: "x", readings: { a: 1 }, config: defaultMachineConfiguration() })).toEqual({ notes: "x" });
    expect(withinScope("full", { readings: { a: 1 } })).toEqual({ readings: { a: 1 } });
  });
});

describe("computeChangesWithPaths", () => {
  it("names the payload field each change came from, as the server divides a payload", () => {
    const base = draftFromPayload(stored(), view());
    const edited: LocalTest = {
      ...base,
      config: { ...base.config, clusterCount: 24 },
      readings: { "tr.workingVacuum": 46 },
      recommendations: { "vp.wick": "Replace" },
      notes: "New",
      pulsatorRows: [{ id: "p1", unit: "1", values: { rate: "60" } }],
    };

    const paths = computeChangesWithPaths(base, edited).map((c) => c.path);

    expect(paths).toEqual(expect.arrayContaining([
      "config.clusterCount", "readings.tr.workingVacuum", "recommendations.vp.wick", "notes", "pulsatorRows",
    ]));
    // The stored record keeps the plain change.
    expect(Object.keys(computeChanges(base, edited)[0]).sort()).toEqual(["from", "label", "section", "to"]);
  });
});
