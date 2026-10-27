// The tester licence reminder, computed on the device from the cached identity record. The
// server-rendered pages draw the same banner in Pages/Shared/_Layout.cshtml — same threshold,
// same words; keep them together. On the device it has one advantage over the server's copy: it
// stays right as days pass offline, because it is counted from today's date here.
//
// It is only a reminder: an expired licence blocks nothing. The tester keeps the whole app, and
// the report shows that the licence had expired by the test date.
import { nzDate } from "../wizard/nextTestDate";

/** Where a tester sends their six tests for renewal, and asks about their licence. */
export const MPNZ_EMAIL = "info@milkingpumping.co.nz";

/** How many days out the warning starts. */
export const LICENCE_WARNING_DAYS = 60;

export interface LicenceBanner {
  /** Leading sentence, shown in bold. */
  lead: string;
  /** The rest. MPNZ_EMAIL in it is drawn as a mailto link. */
  rest: string;
}

const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

/** "07 Oct 2026" — the server's dd MMM yyyy. */
export function formatLicenceDate(iso: string): string {
  const [y, m, d] = iso.split("-").map(Number);
  return `${String(d).padStart(2, "0")} ${MONTHS[m - 1]} ${y}`;
}

/** Whole days from `today` to `expiry` (both ISO yyyy-mm-dd); negative once it has passed. */
export function daysUntil(expiry: string, today: string): number {
  const utc = (iso: string) => {
    const [y, m, d] = iso.split("-").map(Number);
    return Date.UTC(y, m - 1, d);
  };
  return Math.round((utc(expiry) - utc(today)) / 86_400_000);
}

/** The banner for a licence expiring on `expiry` (ISO yyyy-mm-dd), or null when there is nothing
 * to say yet (no date, or more than 60 days away). Days are counted on the New Zealand calendar,
 * whatever zone the device is set to, and the expiry day itself is still licensed. */
export function licenceBanner(
  expiry: string | null | undefined,
  now = new Date(),
  timeZone = "Pacific/Auckland",
): LicenceBanner | null {
  if (!expiry || !/^\d{4}-\d{2}-\d{2}$/.test(expiry)) return null;
  const days = daysUntil(expiry, nzDate(now.toISOString(), timeZone));
  const date = formatLicenceDate(expiry);
  if (days < 0) {
    return { lead: `Your Tester licence expired on ${date}.`, rest: `Contact MPNZ at ${MPNZ_EMAIL} to renew it.` };
  }
  if (days <= LICENCE_WARNING_DAYS) {
    return { lead: `Your Tester licence expires on ${date}`, rest: `— email six tests to ${MPNZ_EMAIL} by this date.` };
  }
  return null;
}
