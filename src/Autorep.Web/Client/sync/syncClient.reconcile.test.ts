import "fake-indexeddb/auto";
import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { allTests, deleteTest, getTest, putTest, type LocalTest } from "../db/testStore";
import { syncAll } from "./syncClient";
import { isReplaced } from "../versioning/chain";
import { defaultMachineConfiguration } from "../wizard/types";

// The device's half of sync reconciliation: an administrator edited a completed test online while
// the tester edited the same test offline. The server answers the tester's signed-off version with
// a 409 carrying the version both were made from and the one already there; the device combines them
// and sends the pair. And the plain case: an administrator's version pulled down locks the original.

const SIGNED = "2026-09-01T00:00:00.000Z";

function v1(): LocalTest {
  return {
    id: "v1",
    farmName: "Kowhai Flats",
    config: defaultMachineConfiguration(),
    currentStep: "ReviewSignOff",
    visualFaults: {},
    attestations: [],
    readings: { "tr.workingVacuum": 48, "tr.nominalVacuum": 50, "tr.regulationDeviation": -2 },
    recommendations: { "vp.wick": "Clean the wicks" },
    dataFields: {},
    notes: "Original comment",
    createdAt: SIGNED,
    updatedAt: SIGNED,
    markedCompleteAt: SIGNED,
    syncState: "uploaded",
    everUploaded: true,
    version: 1,
  };
}

function adminVersion(id: string, version: number, supersedes: string, notes: string): LocalTest {
  return {
    ...v1(),
    id,
    version,
    supersedesId: supersedes,
    notes,
    markedCompleteAt: "2026-10-06T00:00:00.000Z",
    amendments: [{ version, amendedAt: "2026-10-06T00:00:00.000Z", amendedByRole: "Super Administrator", amendedBy: "sam@nzmpta", baseVersion: version - 1, changes: [] }],
  };
}

/** The tester's own version 2, signed off offline and not yet sent. */
function testerV2(): LocalTest {
  return {
    ...v1(),
    id: "tester-v2",
    version: 2,
    supersedesId: "v1",
    readings: { ...v1().readings, "tr.workingVacuum": 46 },
    markedCompleteAt: "2026-10-06T22:00:00.000Z",
    updatedAt: "2026-10-06T22:00:00.000Z",
    syncState: "local-only",
    amendments: [{ version: 2, amendedAt: "2026-10-06T22:00:00.000Z", amendedBy: "ellie@tester", baseVersion: 1, changes: [] }],
  };
}

const summary = (t: LocalTest) => ({
  clientId: t.id,
  farmName: t.farmName,
  createdAt: t.createdAt,
  markedCompleteAt: t.markedCompleteAt ?? null,
  config: t.config,
  payloadJson: JSON.stringify(t),
});

const collision = (head: LocalTest) => ({
  conflict: "superseded",
  baseClientId: "v1",
  base: summary(v1()),
  head: summary(head),
  headVersion: head.version,
});

const EMPTY_PULL = { watermark: SIGNED, tests: [] as unknown[] };

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } });
}

const realFetch = globalThis.fetch;

function stubFetch(handler: (url: string, init?: RequestInit) => Response | Promise<Response>) {
  globalThis.fetch = vi.fn(async (input: unknown, init?: RequestInit) => handler(String(input), init)) as unknown as typeof fetch;
}

interface MergeBody {
  incoming: { clientId: string };
  merged: { clientId: string; version: number; supersedesClientId: string; mergedFromClientId: string; payloadJson: string };
  headClientId: string;
}

beforeEach(async () => {
  for (const t of await allTests()) await deleteTest(t.id);
});

afterEach(() => {
  globalThis.fetch = realFetch;
});

describe("reconciling a version that arrived second", () => {
  it("combines the tester's version with the admin's and sends the pair, keeping both", async () => {
    await putTest(v1());
    await putTest(testerV2());
    const admin = adminVersion("admin-v2", 2, "v1", "Admin's comment");
    const merges: MergeBody[] = [];
    stubFetch((url, init) => {
      if (init?.method === "POST" && url.endsWith("/api/sync/tests")) return json(collision(admin), 409);
      if (init?.method === "POST" && url.endsWith("/api/sync/tests/merge")) {
        const body = JSON.parse(String(init.body)) as MergeBody;
        merges.push(body);
        return json({ status: "merged", id: "server-row", mergedClientId: body.merged.clientId });
      }
      return json(EMPTY_PULL);
    });

    const result = await syncAll();

    expect(result).toEqual({ pushed: 1, failed: 0, pulled: 0, merged: 1 });
    expect(merges).toHaveLength(1);
    const sent = merges[0];
    expect(sent.headClientId).toBe("admin-v2");
    expect(sent.incoming.clientId).toBe("tester-v2");
    expect(sent.merged).toMatchObject({ version: 3, supersedesClientId: "admin-v2", mergedFromClientId: "tester-v2" });
    const combined = JSON.parse(sent.merged.payloadJson) as LocalTest;
    expect(combined.notes).toBe("Admin's comment");
    expect(combined.readings["tr.workingVacuum"]).toBe(46);
    expect(combined.readings["tr.regulationDeviation"]).toBe(-4);

    const all = await allTests();
    const stored = all.find((t) => t.mergedFromId === "tester-v2")!;
    expect(stored).toMatchObject({ id: sent.merged.clientId, syncState: "uploaded", supersedesId: "admin-v2" });
    expect(await getTest("admin-v2")).toBeDefined();
    expect((await getTest("tester-v2"))?.syncState).toBe("uploaded");
    // Both versions it combined are now read-only on the device; the combined one is current.
    expect(isReplaced((await getTest("tester-v2"))!, all)).toBe(true);
    expect(isReplaced((await getTest("admin-v2"))!, all)).toBe(true);
    expect(isReplaced(stored, all)).toBe(false);
  });

  it("combines again when the test moved on before the pair arrived", async () => {
    await putTest(v1());
    await putTest(testerV2());
    const first = adminVersion("admin-v2", 2, "v1", "Admin's comment");
    const second = adminVersion("admin-v3", 3, "admin-v2", "Admin again");
    const merges: MergeBody[] = [];
    stubFetch((url, init) => {
      if (init?.method === "POST" && url.endsWith("/api/sync/tests")) return json(collision(first), 409);
      if (init?.method === "POST" && url.endsWith("/api/sync/tests/merge")) {
        const body = JSON.parse(String(init.body)) as MergeBody;
        merges.push(body);
        return merges.length === 1
          ? json(collision(second), 409)
          : json({ status: "merged", id: "server-row", mergedClientId: body.merged.clientId });
      }
      return json(EMPTY_PULL);
    });

    const result = await syncAll();

    expect(result.merged).toBe(1);
    expect(merges.map((m) => m.headClientId)).toEqual(["admin-v2", "admin-v3"]);
    expect(merges[1].merged).toMatchObject({ version: 4, supersedesClientId: "admin-v3" });
    expect(JSON.parse(merges[1].merged.payloadJson).notes).toBe("Admin again");
  });

  // A signed-off version never changes in place on the server. A copy here that differs from the
  // server's stops trying to send itself, and the pull brings the server's copy back.
  it("lets go of a signed-off copy the server already holds differently, and takes the server's", async () => {
    const mine: LocalTest = { ...v1(), notes: "Local, never meant to differ", syncState: "local-only" };
    await putTest(mine);
    const server = { ...v1(), notes: "As signed off" };
    stubFetch((_url, init) =>
      init?.method === "POST"
        ? json({ error: "completed", fields: ["notes"] }, 409)
        : json({ watermark: SIGNED, tests: [summary(server)] }),
    );

    const result = await syncAll();

    expect(result).toEqual({ pushed: 1, failed: 0, pulled: 1 });
    expect(await getTest("v1")).toMatchObject({ notes: "As signed off", syncState: "uploaded" });
  });

  it("counts a 409 that isn't a collision as an ordinary failed push", async () => {
    await putTest(testerV2());
    stubFetch((_url, init) => (init?.method === "POST" ? json({ error: "something else" }, 409) : json(EMPTY_PULL)));

    const result = await syncAll();

    expect(result).toEqual({ pushed: 0, failed: 1, pulled: 0 });
    expect((await getTest("tester-v2"))?.syncState).toBe("local-only");
  });
});

describe("an administrator's version pulled down", () => {
  it("lands on the device and locks the original it replaces", async () => {
    await putTest(v1());
    const admin = adminVersion("admin-v2", 2, "v1", "Admin's comment");
    stubFetch(() => json({ watermark: SIGNED, tests: [summary(admin)] }));

    const result = await syncAll();

    expect(result.pulled).toBe(1);
    const all = await allTests();
    expect((await getTest("admin-v2"))?.notes).toBe("Admin's comment");
    expect(isReplaced((await getTest("v1"))!, all)).toBe(true);
    expect(isReplaced((await getTest("admin-v2"))!, all)).toBe(false);
  });
});
