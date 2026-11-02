// Kept in its own file: it swaps the store's queue read for one that fails, which the other sync
// specs must not see.
import "fake-indexeddb/auto";
import { describe, it, expect, vi, afterEach } from "vitest";

const failQueueRead = vi.hoisted(() => ({ on: false }));

vi.mock("../db/testStore", async (importOriginal) => {
  const real = await importOriginal<typeof import("../db/testStore")>();
  return {
    ...real,
    referencesWithPrefix: async (prefix: string) => {
      if (failQueueRead.on && prefix === real.QUEUED_FARM_KEY_PREFIX) throw new Error("IndexedDB read failed");
      return real.referencesWithPrefix(prefix);
    },
  };
});

const { putTest } = await import("../db/testStore");
const { syncAll } = await import("./syncClient");
const { defaultMachineConfiguration } = await import("../wizard/types");

const realFetch = globalThis.fetch;
afterEach(() => {
  globalThis.fetch = realFetch;
  failQueueRead.on = false;
});

describe("an unreadable farm queue", () => {
  // Read as empty, it would send a test on a farm still waiting to go up, and the server's safety net
  // would mint a second farm for it with none of the details the tester typed.
  it("stops the sync before any test goes up", async () => {
    const now = "2026-11-02T00:00:00.000Z";
    await putTest({
      id: "t-1", farmName: "Totara Valley", farmId: "farm-new", config: defaultMachineConfiguration(),
      currentStep: "Setup", visualFaults: {}, attestations: [], readings: {}, recommendations: {}, dataFields: {},
      createdAt: now, updatedAt: now, markedCompleteAt: null, syncState: "local-only",
    });
    const posts: string[] = [];
    globalThis.fetch = vi.fn(async (input: unknown, init?: RequestInit) => {
      if (init?.method === "POST") posts.push(String(input));
      return new Response(JSON.stringify({ watermark: now, tests: [] }), { status: 200, headers: { "content-type": "application/json" } });
    }) as unknown as typeof fetch;
    failQueueRead.on = true;

    await expect(syncAll()).rejects.toThrow("IndexedDB read failed");
    expect(posts).toEqual([]);
  });
});
