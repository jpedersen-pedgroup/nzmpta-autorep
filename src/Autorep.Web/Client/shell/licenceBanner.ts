// The tester licence reminder, computed on the device from the cached identity record. The
// server-rendered pages draw the same banner in Pages/Shared/_Layout.cshtml — same thresholds,
// same words; keep them together. On the device it has one advantage over the server's copy: it
// stays right as days pass offline, because it is counted from today's date here.
export interface LicenceBanner {
  urgent: boolean;
  /** Leading sentence, shown in bold. */
  lead: string;
  rest: string;
}

const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

/** "07 Oct 2026" — the server's dd MMM yyyy. */
export function formatLicenceDate(iso: string): string {
  const [y, m, d] = iso.split("-").map(Number);
  return `${String(d).padStart(2, "0")} ${MONTHS[m - 1]} ${y}`;
}

/** Whole days from today (local date) to the expiry date; negative once it has passed. */
export function daysUntil(iso: string, today: Date): number {
  const [y, m, d] = iso.split("-").map(Number);
  const expiry = Date.UTC(y, m - 1, d);
  const now = Date.UTC(today.getFullYear(), today.getMonth(), today.getDate());
  return Math.round((expiry - now) / 86_400_000);
}

/** The banner for a licence expiring on `expiry` (ISO yyyy-mm-dd), or null when there is nothing
 * to say yet (no date, or more than 180 days away). */
export function licenceBanner(expiry: string | null | undefined, today = new Date()): LicenceBanner | null {
  if (!expiry || !/^\d{4}-\d{2}-\d{2}$/.test(expiry)) return null;
  const days = daysUntil(expiry, today);
  const date = formatLicenceDate(expiry);
  if (days <= 0) {
    return {
      urgent: true,
      lead: "Your tester licence has expired.",
      rest: "You can still sign in to send tests already saved on this device, but can't start new ones until it's renewed. Contact NZMPTA.",
    };
  }
  if (days <= 60) {
    return {
      urgent: true,
      lead: `Your tester licence expires in ${days} day${days === 1 ? "" : "s"}`,
      rest: `(${date}). Contact NZMPTA to renew.`,
    };
  }
  if (days <= 180) {
    return { urgent: false, lead: "", rest: `Your tester licence expires on ${date} (in ${days} days). Plan your renewal.` };
  }
  return null;
}
