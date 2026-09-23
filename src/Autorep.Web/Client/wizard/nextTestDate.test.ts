import { describe, it, expect } from "vitest";
import {
  defaultNextTestDate,
  nextTestDateAtSignOff,
  nzDate,
  originalCompletedAt,
  proposedNextTestDate,
} from "./nextTestDate";

describe("next test date", () => {
  it("defaults to twelve months from the New Zealand date of sign-off", () => {
    // 8:41 am NZST on 15 Sep 2026 is 20:41Z on the 14th: the NZ day is the one that counts.
    expect(nzDate("2026-09-14T20:41:48.000Z")).toBe("2026-09-15");
    expect(defaultNextTestDate("2026-09-14T20:41:48.000Z")).toBe("2027-09-15");
  });

  it("lands 29 February on 28 February the next year", () => {
    expect(defaultNextTestDate("2028-02-29T00:00:00.000Z", "UTC")).toBe("2029-02-28");
  });

  it("keeps the tester's choice over the default", () => {
    const now = "2026-09-14T20:41:48.000Z";
    expect(proposedNextTestDate({}, now)).toBe("2027-09-15");
    expect(proposedNextTestDate({ nextTestDate: null }, now)).toBe("2027-09-15");
    expect(proposedNextTestDate({ nextTestDate: "2027-03-01" }, now)).toBe("2027-03-01");
  });
});

describe("next test date on an amendment", () => {
  // The original was signed off on 15 Sep 2026; the amendment (recommendations carried out) on
  // 2 Nov 2026. The farm is still due twelve months from the ORIGINAL test.
  const original = "2026-09-14T20:41:48.000Z";
  const amendedAt = "2026-11-01T22:00:00.000Z";

  it("keeps the superseded version's date, whatever the amendment carries", () => {
    const amendment = { supersedesId: "v1", nextTestDate: "2027-12-25" };
    expect(nextTestDateAtSignOff(amendment, amendedAt, { nextTestDate: "2027-09-15" }, original)).toBe("2027-09-15");
  });

  it("dates an old chain from the original test, not the amendment", () => {
    expect(nextTestDateAtSignOff({ supersedesId: "v1" }, amendedAt, { nextTestDate: null }, original)).toBe("2027-09-15");
  });

  it("leaves an original test's date to the tester", () => {
    expect(nextTestDateAtSignOff({ nextTestDate: "2027-03-01" }, original, null, null)).toBe("2027-03-01");
    expect(nextTestDateAtSignOff({}, original, null, null)).toBe("2027-09-15");
  });

  it("finds the first version's completion from the earliest amendment in the chain", () => {
    expect(originalCompletedAt(undefined)).toBeNull();
    expect(
      originalCompletedAt([
        { version: 3, baseCompletedAt: "2026-12-01T00:00:00.000Z" },
        { version: 2, baseCompletedAt: original },
      ]),
    ).toBe(original);
  });
});
