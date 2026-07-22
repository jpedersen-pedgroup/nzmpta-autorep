// Kept in its own file: purgeStaleLocalData deletes databases by name, which would pull the
// ground out from under the shared store the other testStore specs use.
import "fake-indexeddb/auto";
import { describe, it, expect, beforeEach, vi, afterEach } from "vitest";
import { openDB } from "idb";

const LAST_TESTER_KEY = "autorep:lastTesterId";

function installLocalStorage(): void {
  const store = new Map<string, string>();
  (globalThis as { localStorage?: Storage }).localStorage = {
    getItem: (k: string) => store.get(k) ?? null,
    setItem: (k: string, v: string) => void store.set(k, String(v)),
    removeItem: (k: string) => void store.delete(k),
    clear: () => store.clear(),
    key: (i: number) => [...store.keys()][i] ?? null,
    get length() {
      return store.size;
    },
  } as Storage;
}
installLocalStorage();

function setCurrentTester(id: string | null): void {
  const g = globalThis as { __autorepTesterId?: string };
  if (id === null) delete g.__autorepTesterId;
  else g.__autorepTesterId = id;
}

async function seedTesterDb(testerId: string, syncState: string): Promise<void> {
  const db = await openDB(`autorep_${testerId}`, 2, {
    upgrade(d) {
      if (!d.objectStoreNames.contains("tests")) d.createObjectStore("tests", { keyPath: "id" });
      if (!d.objectStoreNames.contains("reference")) d.createObjectStore("reference", { keyPath: "key" });
    },
  });
  await db.put("tests", { id: "t1", farmName: "Their Farm", syncState });
  db.close();
}

/** Test rows left in a database. A deleted database reopens empty, so this reads 0. */
async function remainingTests(name: string): Promise<number> {
  const db = await openDB(name);
  const count = db.objectStoreNames.contains("tests") ? (await db.getAll("tests")).length : 0;
  db.close();
  return count;
}

const { purgeStaleLocalData } = await import("./testStore");

describe("purgeStaleLocalData", () => {
  // Each case must start from a clean device: the purge now enumerates every autorep_* database,
  // so a leftover from an earlier case would legitimately show up in the next one's result.
  beforeEach(async () => {
    localStorage.clear();
    for (const info of await indexedDB.databases()) {
      if (!info.name?.startsWith("autorep")) continue;
      await new Promise<void>((resolve) => {
        const req = indexedDB.deleteDatabase(info.name!);
        req.onsuccess = req.onerror = req.onblocked = () => resolve();
      });
    }
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  // Identity is server-rendered, so "unknown" means "not established yet" — not "nobody". Purging
  // on that basis wiped the previous tester's queued work every time a page rendered without it.
  it("does nothing when the current tester is unknown", async () => {
    await seedTesterDb("tester-a", "local-only");
    localStorage.setItem(LAST_TESTER_KEY, "tester-a");
    setCurrentTester(null);

    const result = await purgeStaleLocalData();

    expect(result.retained).toBeUndefined();
    expect(await remainingTests("autorep_tester-a")).toBe(1);
    expect(localStorage.getItem(LAST_TESTER_KEY)).toBe("tester-a");
  });

  // A genuine handover: that work exists nowhere else, so it outranks cache hygiene.
  it("keeps a previous tester's database when it holds unsynced work, and reports it", async () => {
    await seedTesterDb("tester-a", "local-only");
    localStorage.setItem(LAST_TESTER_KEY, "tester-a");
    setCurrentTester("tester-b");

    const result = await purgeStaleLocalData();

    expect(result.retained).toEqual([{ testerId: "tester-a", unsyncedCount: 1 }]);
    expect(await remainingTests("autorep_tester-a")).toBe(1);
  });

  it("deletes a previous tester's database once everything has synced", async () => {
    await seedTesterDb("tester-c", "uploaded");
    localStorage.setItem(LAST_TESTER_KEY, "tester-c");
    setCurrentTester("tester-d");

    const result = await purgeStaleLocalData();

    expect(result.retained).toBeUndefined();
    expect(await remainingTests("autorep_tester-c")).toBe(0);
    expect(localStorage.getItem(LAST_TESTER_KEY)).toBe("tester-d");
  });

  // The guard must fail CLOSED. Returning 0 from an unreadable database would hand the delete
  // path a licence to destroy the very work it exists to protect.
  it("keeps a database it cannot read rather than assuming it is empty", async () => {
    await seedTesterDb("tester-e", "local-only");
    localStorage.setItem(LAST_TESTER_KEY, "tester-e");
    setCurrentTester("tester-f");

    const open = indexedDB.open.bind(indexedDB);
    const spy = vi.spyOn(indexedDB, "open").mockImplementation(((name: string, version?: number) => {
      if (name === "autorep_tester-e") throw new DOMException("simulated", "UnknownError");
      return open(name, version);
    }) as typeof indexedDB.open);

    const result = await purgeStaleLocalData();
    spy.mockRestore();

    expect(result.retained).toEqual([{ testerId: "tester-e", unsyncedCount: null }]);
    expect(await remainingTests("autorep_tester-e")).toBe(1);
  });

  // With a single "last tester" slot, a third sign-in hid the second tester's queue entirely:
  // it was never counted, never warned about, and never cleaned up.
  it("checks every previous tester on the device, not just the most recent", async () => {
    await seedTesterDb("tester-g", "local-only");
    await seedTesterDb("tester-h", "local-only");
    localStorage.setItem(LAST_TESTER_KEY, "tester-h");
    setCurrentTester("tester-i");

    const result = await purgeStaleLocalData();

    const ids = (result.retained ?? []).map((r) => r.testerId).sort();
    expect(ids).toEqual(["tester-g", "tester-h"]);
    expect(await remainingTests("autorep_tester-g")).toBe(1);
    expect(await remainingTests("autorep_tester-h")).toBe(1);
  });
});
