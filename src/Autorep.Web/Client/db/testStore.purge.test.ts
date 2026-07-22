// Kept in its own file: purgeStaleLocalData deletes databases by name, which would pull the
// ground out from under the shared store the other testStore specs use.
import "fake-indexeddb/auto";
import { describe, it, expect, beforeEach } from "vitest";
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
  beforeEach(() => {
    localStorage.clear();
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

    expect(result.retained).toEqual({ testerId: "tester-a", unsyncedCount: 1 });
    expect(await remainingTests("autorep_tester-a")).toBe(1);
    // Left pointing at the outgoing tester so the check runs again once that work has synced.
    expect(localStorage.getItem(LAST_TESTER_KEY)).toBe("tester-a");
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
});
