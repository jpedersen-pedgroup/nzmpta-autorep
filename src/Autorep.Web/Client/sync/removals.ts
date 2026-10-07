// What this device knows about tests NZMPTA has soft-deleted (PRD story 70). A deleted test reaches
// the device as a tombstone in the pull (sync/syncClient.ts):
//  - a copy the server already has is removed from the device, and noted here so My tests can say
//    what went and why — a test vanishing without a word would look like lost work;
//  - a copy holding edits the server hasn't seen is NEVER removed: it's flagged here instead, still
//    goes up (the server keeps it with the deleted test), and is removed once it has.
// Both live in the reference store, apart from the tests themselves, so marking a test as deleted
// never rewrites the record the wizard may be saving at that same moment.
import { getReference, putReference } from "../db/testStore";

/** A test removed from this device because NZMPTA deleted it. */
export interface RemovedTest {
  id: string;
  farmName: string;
  version: number;
  deletedAt: string | null;
  reason: string | null;
  /** It held edits the server hadn't seen when the deletion arrived; they were sent first. */
  hadUnsentChanges: boolean;
}

/** When and why NZMPTA deleted a test this device still holds. */
export interface Deletion {
  at: string | null;
  reason: string | null;
}

const REMOVED_KEY = "removedTests";
const DELETED_KEY = "deletedOnServer";

async function read<T>(key: string, fallback: T): Promise<T> {
  try {
    return ((await getReference(key))?.rows as T | undefined) ?? fallback;
  } catch {
    return fallback;
  }
}

/** Tests removed since the tester last dismissed the notice, oldest first. */
export function removedTests(): Promise<RemovedTest[]> {
  return read<RemovedTest[]>(REMOVED_KEY, []);
}

export async function noteRemoved(test: RemovedTest): Promise<void> {
  const list = (await removedTests()).filter((t) => t.id !== test.id);
  await putReference({ key: REMOVED_KEY, rows: [...list, test] });
}

export async function clearRemoved(): Promise<void> {
  await putReference({ key: REMOVED_KEY, rows: [] });
}

/** Tests on this device that NZMPTA has deleted, kept because they hold unsent edits, by id. */
export function deletedOnServer(): Promise<Record<string, Deletion>> {
  return read<Record<string, Deletion>>(DELETED_KEY, {});
}

export async function noteDeletedOnServer(id: string, deletion: Deletion): Promise<void> {
  const map = await deletedOnServer();
  map[id] = deletion;
  await putReference({ key: DELETED_KEY, rows: map });
}

export async function forgetDeletedOnServer(id: string): Promise<void> {
  const map = await deletedOnServer();
  if (!(id in map)) return;
  delete map[id];
  await putReference({ key: DELETED_KEY, rows: map });
}
