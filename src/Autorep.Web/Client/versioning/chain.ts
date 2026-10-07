// A test's versions as this device holds them. Every edit of a completed test is a new version that
// names the one it replaces (supersedesId); an automatic merge also names the version it combined
// (mergedFromId). A replaced version is read-only, whoever replaced it — the tester on this device,
// an administrator online, or a merge — so these links, not a stored flag, decide what's editable.
import type { AmendmentRecord, LocalTest } from "../db/testStore";

/** Ids of the versions something later replaces. */
export function replacedIds(tests: readonly LocalTest[]): Set<string> {
  const ids = new Set<string>();
  for (const t of tests) {
    if (t.supersedesId) ids.add(t.supersedesId);
    if (t.mergedFromId) ids.add(t.mergedFromId);
  }
  return ids;
}

/** Whether a later version replaces this one. */
export function isReplaced(test: LocalTest, tests: readonly LocalTest[]): boolean {
  return tests.some((x) => x.id !== test.id && (x.supersedesId === test.id || x.mergedFromId === test.id));
}

/**
 * Signed-off versions that replace the same version this one does — another edit of the same test,
 * made somewhere else (an administrator's, typically) while this one was being made. While this one
 * is unfinished, it is combined with them when it's signed off and sent.
 */
export function rivalsOf(test: LocalTest, tests: readonly LocalTest[]): LocalTest[] {
  if (!test.supersedesId) return [];
  return tests.filter((x) => x.id !== test.id && x.supersedesId === test.supersedesId && x.markedCompleteAt);
}

/** Whether an unfinished version of the same test, made from the same version, is on this device —
 * starting yet another edit would only fork the test again. */
export function hasUnfinishedSibling(test: LocalTest, tests: readonly LocalTest[]): boolean {
  if (!test.supersedesId) return false;
  return tests.some((x) => x.id !== test.id && x.supersedesId === test.supersedesId && !x.markedCompleteAt);
}

/** The amendment record this version's own sign-off wrote (not one it carries from earlier). */
export function ownAmendment(test: LocalTest): AmendmentRecord | undefined {
  const last = test.amendments?.at(-1);
  return last && last.version === (test.version ?? 1) ? last : undefined;
}

/** Someone other than the tester made this version: an administrator, or an automatic merge. */
export function madeByOther(test: LocalTest): AmendmentRecord | undefined {
  const own = ownAmendment(test);
  return own?.amendedByRole ? own : undefined;
}

/** A version an administrator saved in the admin portal (not a tester's, nor an automatic merge): its
 * stored report is the one made at the save — "as saved" — not one a device sent at sign-off. */
export function savedByAdministrator(test: LocalTest): boolean {
  const own = madeByOther(test);
  return Boolean(own && !own.merge);
}

/** How an amendment record names its author: their name (or login), and role when it isn't the tester. */
export function authorOf(record: AmendmentRecord): string {
  const who = record.amendedByName ?? record.amendedBy ?? "someone";
  return record.amendedByRole && !record.merge ? `${who} (${record.amendedByRole})` : who;
}
