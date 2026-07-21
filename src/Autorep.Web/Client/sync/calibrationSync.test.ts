import "fake-indexeddb/auto";
import { describe, it, expect, beforeEach, vi, afterEach } from "vitest";
import {
  flushCalibration,
  getCachedCalibration,
  initCalibration,
  saveCalibration,
} from "./calibrationSync";
import { putReference } from "../db/testStore";

/** Server responses the tester's profile endpoint can produce, in order of call. */
function mockFetch(handlers: Array<(url: string, init?: RequestInit) => unknown>) {
  const calls: Array<{ url: string; method: string; body?: unknown }> = [];
  let i = 0;
  const fn = vi.fn(async (url: string, init?: RequestInit) => {
    calls.push({
      url,
      method: init?.method ?? "GET",
      body: init?.body ? JSON.parse(String(init.body)) : undefined,
    });
    const handler = handlers[Math.min(i++, handlers.length - 1)];
    const result = handler(url, init);
    if (result instanceof Error) throw result;
    return result;
  });
  vi.stubGlobal("fetch", fn);
  return calls;
}

const ok = (body: unknown) => ({ ok: true, json: async () => body });
const status = (code: number) => ({ ok: false, status: code, json: async () => ({}) });
const offline = () => new Error("Failed to fetch");

async function clearCache() {
  await putReference({ key: "testerCalibration", rows: undefined });
}

beforeEach(clearCache);
afterEach(() => vi.unstubAllGlobals());

describe("initCalibration", () => {
  it("caches the server profile so the wizard can read it offline", async () => {
    mockFetch([() => ok({ airFlowMeters: "2027-01-27", pulsatorTesters: null, vacuumGauges: "2026-08-01" })]);

    await initCalibration();

    expect(await getCachedCalibration()).toEqual({
      airFlowMeters: "2027-01-27",
      pulsatorTesters: null,
      vacuumGauges: "2026-08-01",
    });
  });

  it("leaves the cache untouched when the server is unreachable", async () => {
    await saveCalibration({ airFlowMeters: "2026-05-05" }); // cached (dirty — no fetch stubbed yet)
    mockFetch([() => offline()]);

    await initCalibration();

    expect((await getCachedCalibration()).airFlowMeters).toBe("2026-05-05");
  });

  it("does not poison the cache on a non-ok response (401/403 on a stale session)", async () => {
    mockFetch([() => ok({ airFlowMeters: "2027-01-27" })]);
    await initCalibration();

    mockFetch([() => status(401)]);
    await initCalibration();

    expect((await getCachedCalibration()).airFlowMeters).toBe("2027-01-27");
  });
});

describe("dirty-wins", () => {
  it("keeps a pending offline edit instead of letting the server pull clobber it", async () => {
    // Edit made while offline: the push fails, so the cache stays dirty.
    mockFetch([() => offline()]);
    expect(await saveCalibration({ airFlowMeters: "2026-09-09" })).toBe("offline");

    // Back online, but the flush still fails — the server's older value must NOT replace the edit.
    mockFetch([() => offline()]);
    await initCalibration();

    expect((await getCachedCalibration()).airFlowMeters).toBe("2026-09-09");
  });

  it("flushes the pending edit first, then accepts the refreshed server profile", async () => {
    mockFetch([() => offline()]);
    await saveCalibration({ airFlowMeters: "2026-09-09" });

    // PUT (flush) succeeds, then the GET returns the server's now-updated profile.
    const calls = mockFetch([
      () => ok({}), // PUT
      () => ok({ airFlowMeters: "2026-09-09", pulsatorTesters: "2027-02-02", vacuumGauges: null }), // GET
    ]);
    await initCalibration();

    expect(calls.map((c) => c.method)).toEqual(["PUT", "GET"]);
    expect(calls[0].body).toEqual({
      airFlowMeters: "2026-09-09",
      pulsatorTesters: null,
      vacuumGauges: null,
    });
    // The edit is no longer dirty, so the server copy (incl. the other device's pulsator date) lands.
    expect(await getCachedCalibration()).toEqual({
      airFlowMeters: "2026-09-09",
      pulsatorTesters: "2027-02-02",
      vacuumGauges: null,
    });
  });
});

describe("saveCalibration", () => {
  it("caches immediately and reports synced when the push succeeds", async () => {
    const calls = mockFetch([() => ok({})]);

    expect(await saveCalibration({ airFlowMeters: "2026-12-01" })).toBe("synced");

    expect(calls[0].method).toBe("PUT");
    expect((await getCachedCalibration()).airFlowMeters).toBe("2026-12-01");
  });

  it("sends the complete set so clearing a date reaches the server as null", async () => {
    const calls = mockFetch([() => ok({})]);

    await saveCalibration({ airFlowMeters: null, pulsatorTesters: "2027-03-03" });

    expect(calls[0].body).toEqual({
      airFlowMeters: null,
      pulsatorTesters: "2027-03-03",
      vacuumGauges: null,
    });
  });
});

describe("flushCalibration", () => {
  it("is a no-op when nothing is pending", async () => {
    const calls = mockFetch([() => ok({})]);
    await saveCalibration({ airFlowMeters: "2026-12-01" }); // synced → clean
    calls.length = 0;

    await flushCalibration();

    expect(calls).toHaveLength(0);
  });

  it("re-pushes a pending edit once the tester is back online", async () => {
    mockFetch([() => offline()]);
    await saveCalibration({ vacuumGauges: "2028-04-04" });

    const calls = mockFetch([() => ok({})]);
    await flushCalibration();

    expect(calls[0].method).toBe("PUT");
    expect(calls[0].body).toMatchObject({ vacuumGauges: "2028-04-04" });

    // Now clean — a second flush sends nothing.
    calls.length = 0;
    await flushCalibration();
    expect(calls).toHaveLength(0);
  });

  it("never throws, so a failed calibration push can't break test sync", async () => {
    mockFetch([() => offline()]);
    await saveCalibration({ vacuumGauges: "2028-04-04" });

    mockFetch([() => offline()]);
    await expect(flushCalibration()).resolves.toBeUndefined();
  });
});

describe("getCachedCalibration", () => {
  it("returns an empty set when the device has never synced", async () => {
    expect(await getCachedCalibration()).toEqual({});
  });
});
