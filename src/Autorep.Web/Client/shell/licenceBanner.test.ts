import { describe, it, expect } from "vitest";
import { daysUntil, formatLicenceDate, licenceBanner } from "./licenceBanner";

// Same threshold and words as Pages/Shared/_Layout.cshtml.
const today = new Date("2026-10-07T00:00:00Z"); // 7 Oct 2026, 1 pm in New Zealand

describe("licenceBanner", () => {
  it("says nothing without a date or more than 60 days out", () => {
    expect(licenceBanner(null, today)).toBeNull();
    expect(licenceBanner("2026-12-07", today)).toBeNull(); // 61 days
    expect(licenceBanner("2027-01-15", today)).toBeNull(); // the old 180-day reminder is gone
  });

  it("warns from 60 days out, with the date and where to send six tests", () => {
    const b = licenceBanner("2026-12-06", today)!; // 60 days
    expect(b.lead).toBe("Your Tester licence expires on 06 Dec 2026");
    expect(b.rest).toBe("— email six tests to info@milkingpumping.co.nz by this date.");
  });

  it("treats the expiry day as still licensed, and the day after as expired", () => {
    expect(licenceBanner("2026-10-07", today)!.lead).toBe("Your Tester licence expires on 07 Oct 2026");
    const expired = licenceBanner("2026-10-06", today)!;
    expect(expired.lead).toBe("Your Tester licence expired on 06 Oct 2026.");
    expect(expired.rest).toBe("Contact MPNZ at info@milkingpumping.co.nz to renew it.");
  });

  it("counts on the New Zealand calendar, whatever zone the device is in", () => {
    // 11:30 pm UTC on 6 Oct is already 7 Oct in New Zealand: a licence ending on the 6th has expired.
    const lateUtc = new Date("2026-10-06T23:30:00Z");
    expect(licenceBanner("2026-10-06", lateUtc)!.lead).toBe("Your Tester licence expired on 06 Oct 2026.");
    expect(licenceBanner("2026-10-06", lateUtc, "UTC")!.lead).toBe("Your Tester licence expires on 06 Oct 2026");
  });

  it("ignores a malformed date", () => {
    expect(licenceBanner("07/10/2026", today)).toBeNull();
  });
});

describe("helpers", () => {
  it("formats like the server's dd MMM yyyy", () => {
    expect(formatLicenceDate("2027-03-01")).toBe("01 Mar 2027");
  });
  it("counts whole calendar days", () => {
    expect(daysUntil("2026-10-10", "2026-10-07")).toBe(3);
    expect(daysUntil("2026-10-06", "2026-10-07")).toBe(-1);
  });
});
