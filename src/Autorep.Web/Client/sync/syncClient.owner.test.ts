// Own file: these cases decide whose store the page opens, which is fixed for the module's life.
//
// The offline shell opens the store of whoever the device's identity record names. If the session
// check then says a different tester is signed in (A's sign-in lapsed without a sign-out; B signed
// in on a slow signal), the page reloads as B — but until it does, a sync must not send A's work
// under B's sign-in, where the server would file A's tests as B's.
import "fake-indexeddb/auto";
import { describe, it, expect, afterEach, vi } from "vitest";
import { saveIdentity, type IdentityRecord } from "../db/identity";
import { putTest, storeOwner } from "../db/testStore";
import { StoreOwnerChangedError, syncAll } from "./syncClient";
import { defaultMachineConfiguration } from "../wizard/types";

const record = (testerId: string): IdentityRecord => ({
  testerId,
  displayName: testerId,
  userName: `${testerId}@test.local`,
  roles: ["Tester"],
  certificateNo: null,
  licenceExpiryDate: null,
  syncOnly: false,
  confirmedAt: "2026-10-07T00:00:00.000Z",
});

const realFetch = globalThis.fetch;
afterEach(() => {
  globalThis.fetch = realFetch;
});

describe("a sync when the signed-in tester has changed under the page", () => {
  it("refuses, and sends nothing", async () => {
    await saveIdentity(record("tester-a"));
    await putTest({
      id: "a-unsent",
      farmName: "A's farm",
      config: defaultMachineConfiguration(),
      currentStep: "Setup",
      visualFaults: {},
      attestations: [],
      readings: {},
      recommendations: {},
      dataFields: {},
      createdAt: "2026-10-07T00:00:00.000Z",
      updatedAt: "2026-10-07T00:00:00.000Z",
      markedCompleteAt: null,
      syncState: "local-only",
    });
    expect(storeOwner()).toBe("tester-a");

    // The session check comes back: B is signed in.
    await saveIdentity(record("tester-b"));
    const fetchSpy = vi.fn(async () => new Response("{}", { status: 200 }));
    globalThis.fetch = fetchSpy as unknown as typeof fetch;

    await expect(syncAll()).rejects.toBeInstanceOf(StoreOwnerChangedError);
    expect(fetchSpy).not.toHaveBeenCalled();
  });
});
