import "fake-indexeddb/auto";
import { describe, it, expect, beforeEach, vi, afterEach } from "vitest";
import { getCachedTesterDetails, initTesterDetails } from "./testerDetailsSync";
import { putReference } from "../db/testStore";

function respond(...responses: Array<() => unknown>) {
  let i = 0;
  vi.stubGlobal("fetch", vi.fn(async () => {
    const r = responses[Math.min(i++, responses.length - 1)]();
    if (r instanceof Error) throw r;
    return r;
  }));
}
const ok = (body: unknown) => ({ ok: true, status: 200, json: async () => body });

beforeEach(async () => {
  await putReference({ key: "testerDetails", rows: undefined });
});
afterEach(() => vi.unstubAllGlobals());

describe("tester details sync", () => {
  it("caches the tester's details for offline reports", async () => {
    respond(() => ok({ name: "Alan Tester", phone: "021 752 097", registrationNumber: "594", registrationExpiry: "2026-10-31" }));
    await initTesterDetails();
    expect(await getCachedTesterDetails()).toEqual({
      name: "Alan Tester", phone: "021 752 097", registrationNumber: "594", registrationExpiry: "2026-10-31",
    });
  });

  it("stores blanks as null and ignores a response without a name", async () => {
    respond(() => ok({ name: "Alan Tester", phone: "  ", registrationNumber: null }), () => ok({ name: "" }));
    await initTesterDetails();
    await initTesterDetails();
    expect(await getCachedTesterDetails()).toEqual({ name: "Alan Tester", phone: null, registrationNumber: null, registrationExpiry: null });
  });

  it("keeps the last-known details when offline", async () => {
    respond(() => ok({ name: "Alan Tester" }), () => new Error("Failed to fetch"), () => ({ ok: false, status: 500 }));
    await initTesterDetails();
    await initTesterDetails();
    await initTesterDetails();
    expect((await getCachedTesterDetails())?.name).toBe("Alan Tester");
  });
});
