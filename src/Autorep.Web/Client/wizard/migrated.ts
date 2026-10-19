// Tests migrated from AutoRep Plus say so in a banner wherever they're shown (the admin viewer, the
// tester's own test, a colleague's), with the date AutoRep Plus stopped taking tests once MPNZ has
// set it (the "LegacyTests:CutoffDate" app setting). The server writes that date on the wizard's
// page as data-migrated-cutoff; the offline shell document has no such attribute, so the last value
// a server page gave is remembered on the device. It's a date, not anything that identifies anyone.
import type { LocalTest } from "../db/testStore";

const STORAGE_KEY = "autorep.migratedCutoff";
const ISO_DATE = /^(\d{4})-(\d{2})-(\d{2})$/;
const MONTHS = [
  "January", "February", "March", "April", "May", "June",
  "July", "August", "September", "October", "November", "December",
];

/** A migrated test carries its recommendations as recorded (the same marker the report uses). */
export function isMigrated(test: Pick<LocalTest, "recordedRecommendations">): boolean {
  return test.recordedRecommendations !== undefined;
}

function localStore(): Storage | null {
  try {
    return globalThis.localStorage ?? null;
  } catch {
    return null; // storage blocked: the banner just leaves the date out
  }
}

/** The cutoff the page carries (yyyy-mm-dd), or null while it isn't set. A server page always
 * carries the attribute, empty when unset, and the device remembers what it said; a page without
 * the attribute (the offline shell) falls back to that. */
export function migratedCutoffFrom(
  root: Pick<Element, "getAttribute">,
  storage: Pick<Storage, "getItem" | "setItem" | "removeItem"> | null = localStore(),
): string | null {
  const attr = root.getAttribute("data-migrated-cutoff");
  if (attr === null) {
    try {
      return validDate(storage?.getItem(STORAGE_KEY) ?? null);
    } catch {
      return null;
    }
  }
  const cutoff = validDate(attr);
  try {
    if (cutoff) storage?.setItem(STORAGE_KEY, cutoff);
    else storage?.removeItem(STORAGE_KEY);
  } catch {
    // storage full or blocked: this page still shows the date
  }
  return cutoff;
}

function validDate(value: string | null): string | null {
  const m = value ? ISO_DATE.exec(value.trim()) : null;
  if (!m) return null;
  const month = Number(m[2]);
  const day = Number(m[3]);
  return month >= 1 && month <= 12 && day >= 1 && day <= 31 ? m[0] : null;
}

/** "Migrated from AutoRep Plus — tests before 1 December 2026", or without the date while it's unset. */
export function migratedHeadline(cutoff: string | null | undefined): string {
  const m = ISO_DATE.exec(validDate(cutoff ?? null) ?? "");
  if (!m) return "Migrated from AutoRep Plus";
  return `Migrated from AutoRep Plus — tests before ${Number(m[3])} ${MONTHS[Number(m[2]) - 1]} ${m[1]}`;
}
