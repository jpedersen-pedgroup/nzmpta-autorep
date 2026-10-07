import { describe, it, expect } from "vitest";
import { authorOf, hasUnfinishedSibling, isReplaced, madeByOther, replacedIds, rivalsOf } from "./chain";
import { defaultMachineConfiguration } from "../wizard/types";
import type { LocalTest } from "../db/testStore";

function version(id: string, patch: Partial<LocalTest> = {}): LocalTest {
  const at = "2026-09-01T00:00:00.000Z";
  return {
    id,
    farmName: "Kowhai Flats",
    config: defaultMachineConfiguration(),
    currentStep: "ReviewSignOff",
    visualFaults: {},
    attestations: [],
    readings: {},
    recommendations: {},
    dataFields: {},
    createdAt: at,
    updatedAt: at,
    markedCompleteAt: at,
    syncState: "uploaded",
    version: 1,
    ...patch,
  };
}

const adminV2 = version("admin-v2", {
  version: 2,
  supersedesId: "v1",
  amendments: [{ version: 2, amendedAt: "2026-10-06T00:00:00.000Z", amendedBy: "sam@nzmpta", amendedByName: "Sam Superadmin", amendedByRole: "Super Administrator", baseVersion: 1, changes: [] }],
});

describe("a test's versions on the device", () => {
  it("treats a version as replaced when something later supersedes it or merged it in", () => {
    const merged = version("v3", { version: 3, supersedesId: "admin-v2", mergedFromId: "tester-v2" });

    expect([...replacedIds([version("v1"), adminV2, merged])].sort()).toEqual(["admin-v2", "tester-v2", "v1"]);
  });

  it("locks the original once an administrator's version of it has been pulled", () => {
    const v1 = version("v1");

    expect(isReplaced(v1, [v1])).toBe(false);
    expect(isReplaced(v1, [v1, adminV2])).toBe(true);
  });

  it("finds the administrator's version as a rival of the tester's unfinished edit of the same test", () => {
    const mine = version("tester-v2", { version: 2, supersedesId: "v1", markedCompleteAt: null });
    const all = [version("v1"), adminV2, mine];

    expect(rivalsOf(mine, all).map((t) => t.id)).toEqual(["admin-v2"]);
    expect(hasUnfinishedSibling(adminV2, all)).toBe(true);
    expect(rivalsOf(adminV2, all)).toEqual([]);
  });

  it("says who made a version when it wasn't the tester", () => {
    const own = madeByOther(adminV2)!;

    expect(own.amendedByRole).toBe("Super Administrator");
    expect(authorOf(own)).toBe("Sam Superadmin (Super Administrator)");
    expect(madeByOther(version("v1"))).toBeUndefined();
    // A version that merely carries an earlier version's record wasn't made by that person.
    expect(madeByOther({ ...adminV2, version: 3 })).toBeUndefined();
  });
});
