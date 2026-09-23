// Next Test Date: when the farm is next due a machine test. The tester sets it at sign-off,
// pre-filled to twelve months out and editable (PRD stories 26 and 42), and it prints on page one
// of the report. Stored as an ISO yyyy-mm-dd date — a calendar day, with no time or zone.

/** The New Zealand calendar date of a timestamp, as yyyy-mm-dd. The report's dates are NZ dates
 * whatever zone the device is set to. */
export function nzDate(iso: string, timeZone = "Pacific/Auckland"): string {
  // en-CA is the locale whose numeric date form is yyyy-mm-dd.
  return new Intl.DateTimeFormat("en-CA", { year: "numeric", month: "2-digit", day: "2-digit", timeZone }).format(
    new Date(iso),
  );
}

/** Twelve months on from the NZ date of `fromIso`. 29 February lands on 28 February, not in March. */
export function defaultNextTestDate(fromIso: string, timeZone = "Pacific/Auckland"): string {
  const [y, m, d] = nzDate(fromIso, timeZone).split("-").map(Number);
  const lastDay = new Date(Date.UTC(y + 1, m, 0)).getUTCDate();
  return `${y + 1}-${String(m).padStart(2, "0")}-${String(Math.min(d, lastDay)).padStart(2, "0")}`;
}

/** What sign-off will record: the tester's choice, else twelve months from `nowIso`. */
export function proposedNextTestDate(test: { nextTestDate?: string | null }, nowIso: string): string {
  return test.nextTestDate ?? defaultNextTestDate(nowIso);
}
