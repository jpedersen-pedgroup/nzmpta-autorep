import "fake-indexeddb/auto";
import { describe, it, expect, beforeEach } from "vitest";
import { openDB } from "idb";
import {
  IDENTITY_DB,
  cachedIdentity,
  clearIdentity,
  homeFor,
  identityFromSession,
  loadIdentity,
  normaliseIdentity,
  saveIdentity,
  type IdentityRecord,
} from "./identity";

const SAM: IdentityRecord = {
  testerId: "t-sam",
  displayName: "Sam Tester",
  userName: "sam@test.local",
  roles: ["Tester"],
  certificateNo: "NZ-1",
  licenceExpiryDate: "2027-03-01",
  syncOnly: false,
  confirmedAt: "2026-10-07T00:00:00.000Z",
};

beforeEach(async () => {
  await clearIdentity();
});

describe("identity record", () => {
  it("is saved to its own database and readable synchronously afterwards", async () => {
    await saveIdentity(SAM);
    expect(cachedIdentity()).toEqual(SAM);

    const db = await openDB(IDENTITY_DB);
    expect(await db.get("identity", "current")).toEqual(SAM);
    db.close();
  });

  it("is read back from storage at boot — what a cold launch with no signal runs on", async () => {
    const db = await openDB(IDENTITY_DB, 1, { upgrade: (d) => void d.createObjectStore("identity") });
    await db.put("identity", { ...SAM, testerId: "t-written-earlier" }, "current");
    db.close();

    expect((await loadIdentity())?.testerId).toBe("t-written-earlier");
    expect(cachedIdentity()?.testerId).toBe("t-written-earlier");
  });

  it("is gone after clearIdentity — the sign-out path", async () => {
    await saveIdentity(SAM);
    await clearIdentity();
    expect(cachedIdentity()).toBeNull();
    expect(await loadIdentity()).toBeNull();
  });

  it("lives in a database the per-tester purge can never mistake for a tester's store", () => {
    // purgeStaleLocalData deletes every autorep_<id> database it finds that holds no unsynced tests.
    expect(IDENTITY_DB.startsWith("autorep_")).toBe(false);
  });

  it("reads as nobody when the stored value isn't a record", async () => {
    expect(normaliseIdentity(null)).toBeNull();
    expect(normaliseIdentity({ displayName: "No id" })).toBeNull();
    expect(normaliseIdentity({ testerId: "  " })).toBeNull();
  });

  it("keeps only a well-formed licence date", () => {
    expect(normaliseIdentity({ ...SAM, licenceExpiryDate: "01/03/2027" })?.licenceExpiryDate).toBeNull();
    expect(normaliseIdentity({ ...SAM, licenceExpiryDate: "2027-03-01" })?.licenceExpiryDate).toBe("2027-03-01");
  });

  it("takes a /api/session body and stamps when the server confirmed it", () => {
    const now = new Date("2026-10-07T09:30:00.000Z");
    const record = identityFromSession(
      {
        testerId: "t-1",
        displayName: "",
        userName: "one@test.local",
        roles: ["Tester", 7],
        certificateNo: null,
        licenceExpiryDate: null,
        syncOnly: true,
        serverTime: "ignored",
      },
      now,
    );
    expect(record).toEqual({
      testerId: "t-1",
      displayName: "one@test.local", // blank display name falls back to the login
      userName: "one@test.local",
      roles: ["Tester"],
      certificateNo: null,
      licenceExpiryDate: null,
      syncOnly: true,
      confirmedAt: now.toISOString(),
    });
  });
});

describe("homeFor — the offline twin of Pages/Index.cshtml.cs", () => {
  it("sends a tester to the tester app", () => {
    expect(homeFor(SAM, new Date(2026, 9, 7))).toBe("tester");
  });
  it("sends a lapsed licence to the sync-only page", () => {
    expect(homeFor({ ...SAM, syncOnly: true })).toBe("sync-only");
  });
  it("has nowhere offline for a pure administrator — the portal is online-only", () => {
    expect(homeFor({ ...SAM, roles: ["CompanyAdministrator"] })).toBe("admin");
  });
  it("sends an administrator who also tests to the tester app (it's all that works offline)", () => {
    expect(homeFor({ ...SAM, roles: ["SuperAdministrator", "Tester"] })).toBe("tester");
  });
  // Codex review of #73: the record is written online, and a licence can lapse while the device is
  // offline — so the shell decides sync-only from the date too, by the server's own rule.
  it("treats a pure tester whose licence has lapsed as sync-only, whatever the record says", () => {
    const today = new Date(2026, 9, 7);
    expect(homeFor({ ...SAM, licenceExpiryDate: "2026-10-06" }, today)).toBe("sync-only");
    expect(homeFor({ ...SAM, licenceExpiryDate: "2026-10-07" }, today)).toBe("tester"); // the last day still counts
    expect(homeFor({ ...SAM, licenceExpiryDate: null }, today)).toBe("tester");
  });

  it("never makes an administrator who also tests sync-only — the server doesn't either", () => {
    const today = new Date(2026, 9, 7);
    expect(homeFor({ ...SAM, roles: ["CompanyAdministrator", "Tester"], licenceExpiryDate: "2026-01-01" }, today)).toBe("tester");
  });

  it("knows nobody when no one has signed in here", () => {
    expect(homeFor(null)).toBe("nobody");
    expect(homeFor({ ...SAM, roles: [] })).toBe("nobody");
  });
});

