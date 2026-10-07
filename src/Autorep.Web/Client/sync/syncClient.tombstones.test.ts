import "fake-indexeddb/auto";
import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { allTests, deleteTest, getTest, putReference, putTest, type LocalTest } from "../db/testStore";
import { syncAll } from "./syncClient";
import { deletedOnServer, removedTests } from "./removals";
import { defaultMachineConfiguration } from "../wizard/types";

// PRD story 70 on the device: a test NZMPTA soft-deleted arrives as a tombstone in the pull. A copy the
// server already has is removed (and the tester told); a copy holding unsent edits is never deleted —
// it's flagged, still sent (the server keeps it with the deleted test), and removed once it's clean.

const SIGNED = "2026-09-01T00:00:00.000Z";
const DELETED_AT = "2026-10-07T04:00:00.000Z";

function test(id: string, patch: Partial<LocalTest> = {}): LocalTest {
  return {
    id,
    farmName: "Kowhai Flats",
    config: defaultMachineConfiguration(),
    currentStep: "ReviewSignOff",
    visualFaults: {},
    attestations: [],
    readings: {},
    recommendations: {},
    dataFields: {},
    createdAt: SIGNED,
    updatedAt: SIGNED,
    markedCompleteAt: SIGNED,
    syncState: "uploaded",
    everUploaded: true,
    version: 1,
    ...patch,
  };
}

const tombstone = (t: LocalTest) => ({
  clientId: t.id,
  farmName: t.farmName,
  createdAt: t.createdAt,
  markedCompleteAt: t.markedCompleteAt ?? null,
  config: t.config,
  payloadJson: JSON.stringify(t),
  deleted: true,
  deletedAt: DELETED_AT,
  deletedReason: "Entered against the wrong farm",
});

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } });
}

const realFetch = globalThis.fetch;

function stubFetch(handler: (url: string, init?: RequestInit) => Response | Promise<Response>) {
  globalThis.fetch = vi.fn(async (input: unknown, init?: RequestInit) => handler(String(input), init)) as unknown as typeof fetch;
}

beforeEach(async () => {
  for (const t of await allTests()) await deleteTest(t.id);
  await putReference({ key: "removedTests", rows: [] });
  await putReference({ key: "deletedOnServer", rows: {} });
  await putReference({ key: "testPullWatermark", version: null });
});

afterEach(() => {
  globalThis.fetch = realFetch;
});

describe("a deleted test reaching the device", () => {
  it("removes every clean version from the device and says what went and why", async () => {
    const v1 = test("v1");
    const v2 = test("v2", { version: 2, supersedesId: "v1" });
    await putTest(v1);
    await putTest(v2);
    stubFetch(() => json({ watermark: SIGNED, tests: [tombstone(v1), tombstone(v2)] }));

    const result = await syncAll();

    expect(result).toEqual({ pushed: 0, failed: 0, pulled: 0, removed: 2 });
    expect(await allTests()).toEqual([]);
    const removed = await removedTests();
    expect(removed.map((r) => r.id).sort()).toEqual(["v1", "v2"]);
    expect(removed[0]).toMatchObject({ farmName: "Kowhai Flats", reason: "Entered against the wrong farm", hadUnsentChanges: false });
  });

  it("never deletes a copy with unsent edits: keeps it, flags it, sends it, then removes it", async () => {
    // A draft (a signed-off version can't hold unsent edits).
    const dirty = test("v1", { notes: "Edited offline", syncState: "local-only", markedCompleteAt: null, updatedAt: "2026-10-07T03:00:00.000Z" });
    await putTest(dirty);
    let pushes = 0;
    // First sync: the push fails (offline mid-way), the pull carries the tombstone.
    stubFetch((_url, init) => (init?.method === "POST" ? json({ error: "busy" }, 503) : json({ watermark: SIGNED, tests: [tombstone(dirty)] })));

    await syncAll();

    expect((await getTest("v1"))?.notes).toBe("Edited offline");
    expect(await deletedOnServer()).toEqual({ v1: { at: DELETED_AT, reason: "Entered against the wrong farm" } });

    // Next sync: the push lands (the server keeps it with the deleted test), and the pull — the row's
    // UpdatedAt moved — brings the tombstone again, which now finds a clean copy.
    stubFetch((_url, init) => {
      if (init?.method === "POST") {
        pushes++;
        return json({ id: "row", status: "deleted", deletedAt: DELETED_AT, reason: "Entered against the wrong farm" });
      }
      return json({ watermark: SIGNED, tests: [tombstone(dirty)] });
    });

    const result = await syncAll();

    expect(pushes).toBe(1);
    expect(result.removed).toBe(1);
    expect(await getTest("v1")).toBeUndefined();
    expect(await deletedOnServer()).toEqual({});
    expect((await removedTests())[0]).toMatchObject({ id: "v1", hadUnsentChanges: true });
  });

  it("ignores a tombstone for a test this device never held", async () => {
    stubFetch(() => json({ watermark: SIGNED, tests: [tombstone(test("elsewhere"))] }));

    const result = await syncAll();

    expect(result).toEqual({ pushed: 0, failed: 0, pulled: 0 });
    expect(await removedTests()).toEqual([]);
  });

  it("forgets the flag when the test is restored", async () => {
    const dirty = test("v1", { syncState: "local-only", markedCompleteAt: null });
    await putTest(dirty);
    stubFetch((_url, init) => (init?.method === "POST" ? json({ error: "busy" }, 503) : json({ watermark: SIGNED, tests: [tombstone(dirty)] })));
    await syncAll();
    expect(Object.keys(await deletedOnServer())).toEqual(["v1"]);

    const restored = { ...tombstone(dirty), deleted: false, deletedAt: null, deletedReason: null };
    stubFetch((_url, init) => (init?.method === "POST" ? json({ error: "busy" }, 503) : json({ watermark: SIGNED, tests: [restored] })));
    await syncAll();

    expect(await deletedOnServer()).toEqual({});
    expect((await getTest("v1"))?.syncState).toBe("local-only");
  });
});
