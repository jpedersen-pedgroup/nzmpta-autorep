// Syncs can now start on their own while the tester is working. Two things must hold: an edit made
// while a push is on the wire is never lost or wrongly marked sent, and syncs never overlap.
import "fake-indexeddb/auto";
import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { allTests, deleteTest, getTest, putTest, type LocalTest } from "../db/testStore";
import { syncAll } from "./syncClient";
import { defaultMachineConfiguration } from "../wizard/types";

function sample(id: string): LocalTest {
  const now = "2026-10-07T00:00:00.000Z";
  return {
    id,
    farmName: "Rimu Ridge",
    config: defaultMachineConfiguration(),
    currentStep: "Setup",
    visualFaults: {},
    attestations: [],
    readings: {},
    recommendations: {},
    dataFields: {},
    createdAt: now,
    updatedAt: now,
    markedCompleteAt: null,
    syncState: "local-only",
  };
}

const EMPTY_PULL = { watermark: "2026-10-07T00:00:00.000Z", tests: [], next: null };
const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } });

const realFetch = globalThis.fetch;
beforeEach(async () => {
  for (const t of await allTests()) await deleteTest(t.id);
});
afterEach(() => {
  globalThis.fetch = realFetch;
});

describe("a push racing an edit", () => {
  it("keeps the edit, and keeps it queued to send — it was not what went up", async () => {
    await putTest(sample("t1"));
    globalThis.fetch = vi.fn(async (_url: unknown, init?: RequestInit) => {
      if (init?.method === "POST") {
        // The tester types while the push is on the wire.
        const t = (await getTest("t1"))!;
        await putTest({ ...t, readings: { "1a": 50 }, updatedAt: "2026-10-07T00:00:05.000Z" });
        return json({ id: "s1", status: "created" }, 201);
      }
      return json(EMPTY_PULL);
    }) as unknown as typeof fetch;

    await syncAll();

    const after = (await getTest("t1"))!;
    expect(after.readings).toEqual({ "1a": 50 });
    expect(after.syncState).toBe("local-only");
    expect(after.everUploaded).toBe(true); // the server has an earlier copy: no longer deletable
  });

  it("marks a test sent when nothing changed while it was on the wire", async () => {
    await putTest(sample("t2"));
    globalThis.fetch = vi.fn(async (_url: unknown, init?: RequestInit) =>
      init?.method === "POST" ? json({ id: "s2", status: "created" }, 201) : json(EMPTY_PULL),
    ) as unknown as typeof fetch;

    await syncAll();

    expect((await getTest("t2"))?.syncState).toBe("uploaded");
  });
});

describe("one sync at a time", () => {
  it("runs a call made mid-sync once more afterwards, shared by every caller meanwhile", async () => {
    await putTest(sample("t3"));
    let posts = 0;
    let releaseFirst!: () => void;
    const firstPush = new Promise<void>((r) => (releaseFirst = r));
    globalThis.fetch = vi.fn(async (_url: unknown, init?: RequestInit) => {
      if (init?.method === "POST") {
        posts++;
        if (posts === 1) await firstPush;
        return json({ id: "s3", status: "created" }, 201);
      }
      return json(EMPTY_PULL);
    }) as unknown as typeof fetch;

    const first = syncAll();
    await new Promise((r) => setTimeout(r, 20));
    // Marked complete meanwhile — the running sync already read the old copy.
    const t = (await getTest("t3"))!;
    await putTest({ ...t, markedCompleteAt: "2026-10-07T01:00:00.000Z", updatedAt: "2026-10-07T01:00:00.000Z" });
    const second = syncAll();
    const third = syncAll();
    expect(third).toBe(second);

    releaseFirst();
    await first;
    await second;

    expect(posts).toBe(2);
    const after = (await getTest("t3"))!;
    expect(after.markedCompleteAt).toBe("2026-10-07T01:00:00.000Z");
    expect(after.syncState).toBe("uploaded");
  });
});
