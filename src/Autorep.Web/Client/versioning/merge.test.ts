import { describe, it, expect } from "vitest";
import { mergeVersions } from "./merge";
import { defaultMachineConfiguration } from "../wizard/types";
import type { AmendmentRecord, LocalTest } from "../db/testStore";

const SIGNED = "2026-09-01T00:00:00.000Z";
const MERGED_AT = "2026-10-07T03:00:00.000Z";

/** Version 1: a completed test as it was signed off. */
function v1(): LocalTest {
  return {
    id: "v1",
    farmName: "Kowhai Flats",
    config: { ...defaultMachineConfiguration(), clusterCount: 20 },
    currentStep: "ReviewSignOff",
    visualFaults: { "vp.wick": { status: "fault", severity: "Minor", observation: "Oil Wicks Dirty" } },
    attestations: [{ step: "ReviewSignOff", attestedAt: SIGNED, text: "I confirm this test has been completed." }],
    readings: { "tr.workingVacuum": 48, "tr.nominalVacuum": 50, "tr.regulationDeviation": -2 },
    recommendations: { "vp.wick": "Clean the wicks" },
    dataFields: {},
    notes: "Original comment",
    createdAt: SIGNED,
    updatedAt: SIGNED,
    markedCompleteAt: SIGNED,
    syncState: "uploaded",
    version: 1,
    testedBy: { name: "Ellie Tester" },
  };
}

function record(version: number, extra: Partial<AmendmentRecord> = {}): AmendmentRecord {
  return { version, amendedAt: "2026-10-06T00:00:00.000Z", baseVersion: version - 1, changes: [], ...extra };
}

/** An administrator's version 2, made online. */
function adminV2(patch: Partial<LocalTest>): LocalTest {
  return {
    ...v1(),
    ...patch,
    id: "admin-v2",
    version: 2,
    supersedesId: "v1",
    markedCompleteAt: "2026-10-06T00:00:00.000Z",
    amendments: [record(2, { amendedBy: "sam@nzmpta", amendedByName: "Sam Superadmin", amendedByRole: "Super Administrator", reason: "Wording" })],
  };
}

/** The tester's own version 2, made offline from the same version 1. */
function testerV2(patch: Partial<LocalTest>): LocalTest {
  return {
    ...v1(),
    ...patch,
    id: "tester-v2",
    version: 2,
    supersedesId: "v1",
    markedCompleteAt: "2026-10-06T22:00:00.000Z",
    attestations: [{ step: "ReviewSignOff", attestedAt: "2026-10-06T22:00:00.000Z", text: "I confirm this test has been completed." }],
    amendments: [record(2, { amendedBy: "ellie@tester" })],
  };
}

const opts = { id: "merged-v3", now: MERGED_AT, mergedBy: "ellie@tester" };

describe("mergeVersions", () => {
  it("combines edits to different fields — the admin's recommendation and the tester's reading", () => {
    const head = adminV2({ recommendations: { "vp.wick": "Replace the wicks" } });
    const incoming = testerV2({ readings: { ...v1().readings, "tr.workingVacuum": 46 } });

    const { merged, overlapping } = mergeVersions(v1(), head, incoming, opts);

    expect(merged.recommendations["vp.wick"]).toBe("Replace the wicks");
    expect(merged.readings["tr.workingVacuum"]).toBe(46);
    expect(overlapping).toEqual([]);
    expect(merged.amendments?.at(-1)?.merge?.overlaps).toEqual([]);
  });

  it("where both changed the same field, keeps the later arrival's value and records both", () => {
    const head = adminV2({ notes: "Admin's comment" });
    const incoming = testerV2({ notes: "Tester's comment" });

    const { merged, overlapping } = mergeVersions(v1(), head, incoming, opts);

    expect(merged.notes).toBe("Tester's comment");
    expect(overlapping).toEqual(["notes"]);
    expect(merged.amendments?.at(-1)?.merge?.overlaps).toEqual([
      {
        section: "Other",
        label: "General comments",
        previous: "Original comment",
        head: "Admin's comment",
        incoming: "Tester's comment",
        kept: "incoming",
      },
    ]);
  });

  it("doesn't count the same change made on both sides as a clash", () => {
    const head = adminV2({ notes: "Fixed" });
    const incoming = testerV2({ notes: "Fixed" });

    const { merged, overlapping } = mergeVersions(v1(), head, incoming, opts);

    expect(merged.notes).toBe("Fixed");
    expect(overlapping).toEqual([]);
  });

  it("recomputes calculated readings from the merged inputs rather than taking either side's", () => {
    // The admin corrected 1b, the tester 1a; 1c (1a − 1b) must follow both.
    const head = adminV2({ readings: { ...v1().readings, "tr.nominalVacuum": 49, "tr.regulationDeviation": -1 } });
    const incoming = testerV2({ readings: { ...v1().readings, "tr.workingVacuum": 46, "tr.regulationDeviation": -4 } });

    const { merged, overlapping } = mergeVersions(v1(), head, incoming, opts);

    expect(merged.readings["tr.workingVacuum"]).toBe(46);
    expect(merged.readings["tr.nominalVacuum"]).toBe(49);
    expect(merged.readings["tr.regulationDeviation"]).toBe(-3);
    expect(overlapping).toEqual([]);
  });

  it("is the next version, supersedes the head, names the version merged in and extends the head's history", () => {
    const head = adminV2({ notes: "Admin's" });
    const incoming = testerV2({ readings: { ...v1().readings, "tr.workingVacuum": 46 } });

    const { merged } = mergeVersions(v1(), head, incoming, opts);

    expect(merged).toMatchObject({
      id: "merged-v3",
      version: 3,
      supersedesId: "admin-v2",
      mergedFromId: "tester-v2",
      // As complete as the later of the two sign-offs it combines; the merge's own time is its record's.
      markedCompleteAt: "2026-10-06T22:00:00.000Z",
      createdAt: MERGED_AT,
      syncState: "local-only",
    });
    const history = merged.amendments ?? [];
    expect(history.map((a) => a.version)).toEqual([2, 3]);
    expect(history[0].reason).toBe("Wording");
    const own = history[1];
    expect(own).toMatchObject({ amendedBy: "ellie@tester", amendedByRole: "Automatic merge", baseVersion: 2 });
    expect(own.merge).toMatchObject({ headVersion: 2, headBy: "Sam Superadmin", fromVersion: 2, fromId: "tester-v2", fromBy: "ellie@tester" });
    // What the merge changed relative to the head: the tester's reading.
    expect(own.changes).toContainEqual({
      section: "Numerical readings",
      label: "Working vacuum @ receiver (1a)",
      from: "48 kPa",
      to: "46 kPa",
    });
  });

  it("keeps both versions' attestations, each once", () => {
    const { merged } = mergeVersions(v1(), adminV2({}), testerV2({}), opts);

    expect(merged.attestations.map((a) => a.attestedAt)).toEqual([SIGNED, "2026-10-06T22:00:00.000Z"]);
  });

  it("takes the head's analyser PDF as a pointer to the head's server copy", () => {
    const pdf = { name: "pulse.pdf", size: 2048, attachedAt: "2026-10-06T00:00:00.000Z", onServer: true };
    const head = adminV2({ pulsationPdf: pdf });

    const { merged } = mergeVersions(v1(), head, testerV2({}), opts);

    expect(merged.pulsationPdf).toMatchObject({ name: "pulse.pdf", onServer: true, serverTestId: "admin-v2" });
  });

  it("keeps who did the test and where from the head, whatever the incoming version says", () => {
    const { merged } = mergeVersions(v1(), adminV2({}), testerV2({ farmName: "Somewhere Else", testedBy: { name: "Nobody" } }), opts);

    expect(merged.farmName).toBe("Kowhai Flats");
    expect(merged.testedBy?.name).toBe("Ellie Tester");
  });

  it("is deterministic", () => {
    const head = adminV2({ notes: "Admin's", recommendations: { "vp.wick": "Replace" } });
    const incoming = testerV2({ notes: "Tester's", readings: { ...v1().readings, "tr.workingVacuum": 47 } });

    expect(mergeVersions(v1(), head, incoming, opts)).toEqual(mergeVersions(v1(), head, incoming, opts));
  });
});
