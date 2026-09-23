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

/** The date sign-off records. An original test takes the tester's choice, else twelve months from
 * sign-off. An amendment (a new version recording recommendations carried out, or a correction)
 * never moves it: the date belongs to the original test. It keeps the superseded version's date,
 * and a chain whose original was signed off before the date existed gets twelve months from the
 * ORIGINAL test's completion, not from the amendment's.
 * `base` is the version being superseded; `originalCompletedAt` the first version's completion. */
export function nextTestDateAtSignOff(
  test: { nextTestDate?: string | null; supersedesId?: string | null },
  nowIso: string,
  base?: { nextTestDate?: string | null } | null,
  originalCompletedAt?: string | null,
): string {
  if (!test.supersedesId) return proposedNextTestDate(test, nowIso);
  return base?.nextTestDate ?? test.nextTestDate ?? defaultNextTestDate(originalCompletedAt ?? nowIso);
}

/** When the first version of an amended test was completed: the base of the earliest amendment in
 * the chain. Null when there's no amendment record to read it from. */
export function originalCompletedAt(amendments: { version: number; baseCompletedAt?: string | null }[] | undefined): string | null {
  const first = [...(amendments ?? [])].sort((a, b) => a.version - b.version)[0];
  return first?.baseCompletedAt ?? null;
}
