import { describe, it, expect } from "vitest";
import { defaultNextTestDate, nzDate, proposedNextTestDate } from "./nextTestDate";

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
