// currentTesterId's fallback to the device's identity record — the offline shell carries no
// identity of its own. Own file: these specs swap the global identity around, which the shared
// testStore specs rely on staying put.
import "fake-indexeddb/auto";
import { describe, it, expect, afterEach } from "vitest";
import { clearIdentity, saveIdentity, type IdentityRecord } from "./identity";
import { currentTesterId, currentTesterName } from "./testStore";

// purgeStaleLocalData tracks known testers in localStorage, which Node doesn't provide.
const memory = new Map<string, string>();
(globalThis as { localStorage?: Storage }).localStorage = {
  getItem: (k: string) => memory.get(k) ?? null,
  setItem: (k: string, v: string) => void memory.set(k, String(v)),
  removeItem: (k: string) => void memory.delete(k),
  clear: () => memory.clear(),
  key: (i: number) => [...memory.keys()][i] ?? null,
  get length() {
    return memory.size;
  },
} as Storage;

const record = (testerId: string): IdentityRecord => ({
  testerId,
  displayName: "Cached Tester",
  userName: `${testerId}@test.local`,
  roles: ["Tester"],
  certificateNo: null,
  licenceExpiryDate: null,
  syncOnly: false,
  confirmedAt: "2026-10-07T00:00:00.000Z",
});

const page = globalThis as { __autorepTesterId?: string; __autorepTesterName?: string };

afterEach(async () => {
  delete page.__autorepTesterId;
  delete page.__autorepTesterName;
  await clearIdentity();
});

describe("currentTesterId / currentTesterName", () => {
  it("use the server-rendered page's identity when there is one", async () => {
    await saveIdentity(record("t-cached"));
    page.__autorepTesterId = "t-server";
    page.__autorepTesterName = "server@test.local";
    expect(currentTesterId()).toBe("t-server");
    expect(currentTesterName()).toBe("server@test.local");
  });

  it("fall back to the identity record in the offline shell", async () => {
    await saveIdentity(record("t-cached"));
    expect(currentTesterId()).toBe("t-cached");
    expect(currentTesterName()).toBe("t-cached@test.local");
  });

  it("stay unknown — never empty-string — when neither exists", () => {
    expect(currentTesterId()).toBeNull();
    expect(currentTesterName()).toBeUndefined();
  });
});

describe("purgeStaleLocalData in the offline shell", () => {
  it("runs on the identity record: keeps this tester's store, clears a previous tester's synced one", async () => {
    const { openDB } = await import("idb");
    const { purgeStaleLocalData } = await import("./testStore");
    const seed = async (testerId: string, syncState: string) => {
      const db = await openDB(`autorep_${testerId}`, 2, {
        upgrade(d) {
          d.createObjectStore("tests", { keyPath: "id" });
          d.createObjectStore("reference", { keyPath: "key" });
        },
      });
      await db.put("tests", { id: "x", farmName: "F", syncState });
      db.close();
    };
    const names = async () => (await indexedDB.databases()).map((d) => d.name);

    await seed("t-shell", "local-only");
    await seed("t-previous", "uploaded");
    await saveIdentity(record("t-shell")); // no page global: this is all the shell knows

    await purgeStaleLocalData();

    expect(await names()).toContain("autorep_t-shell");
    expect(await names()).not.toContain("autorep_t-previous");
  });
});
