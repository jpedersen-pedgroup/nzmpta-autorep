import { describe, it, expect } from "vitest";
import { daysUntil, formatLicenceDate, licenceBanner } from "./licenceBanner";

// Same thresholds and words as Pages/Shared/_Layout.cshtml.
const today = new Date(2026, 9, 7); // 7 Oct 2026, local

describe("licenceBanner", () => {
  it("says nothing without a date or more than 180 days out", () => {
    expect(licenceBanner(null, today)).toBeNull();
    expect(licenceBanner("2027-06-01", today)).toBeNull();
  });

  it("gives a plain reminder inside 180 days", () => {
    const b = licenceBanner("2027-01-15", today)!;
    expect(b.urgent).toBe(false);
    expect(b.rest).toBe("Your tester licence expires on 15 Jan 2027 (in 100 days). Plan your renewal.");
  });

  it("turns urgent inside 60 days, with the date and a singular day", () => {
    const b = licenceBanner("2026-10-08", today)!;
    expect(b.urgent).toBe(true);
    expect(b.lead).toBe("Your tester licence expires in 1 day");
    expect(b.rest).toBe("(08 Oct 2026). Contact NZMPTA to renew.");
  });

  it("says expired on the day and after — and keeps counting while offline", () => {
    expect(licenceBanner("2026-10-07", today)!.lead).toBe("Your tester licence has expired.");
    expect(licenceBanner("2026-09-30", today)!.urgent).toBe(true);
  });

  it("ignores a malformed date", () => {
    expect(licenceBanner("07/10/2026", today)).toBeNull();
  });
});

describe("helpers", () => {
  it("formats like the server's dd MMM yyyy", () => {
    expect(formatLicenceDate("2027-03-01")).toBe("01 Mar 2027");
  });
  it("counts whole calendar days, unaffected by the time of day", () => {
    expect(daysUntil("2026-10-10", new Date(2026, 9, 7, 23, 59))).toBe(3);
  });
});
