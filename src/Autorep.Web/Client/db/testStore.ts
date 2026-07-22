// IndexedDB store for the offline Tester PWA. For now it holds the Tester's Machine Tests
// (created/edited on-device); later phases add reference-data, vendor-spec snapshots and cached
// Final Report blobs. Wraps `idb` for a small typed surface.
import { openDB, type DBSchema, type IDBPDatabase } from "idb";
import type { ChecklistAttestation, MachineConfiguration, VisualFaultEntry } from "../wizard/types";

export type SyncState = "local-only" | "uploading" | "uploaded" | "merge-conflict";

/** Read-only snapshot of the chosen Farm's details (from /api/farms/{id}), held on the test
 * so the wizard can show them offline and carry them on sync. */
export interface FarmSnapshot {
  name: string;
  supplyNumber?: string | null;
  addressLine1?: string | null;
  addressLine2?: string | null;
  town?: string | null;
  postCode?: string | null;
  rapidNumber?: string | null;
  regionName?: string | null;
  milkCompanyName?: string | null;
  farmerName?: string | null;
  contactPhone?: string | null;
  contactEmail?: string | null;
}

/** The pulsation analyser's exported PDF, attached on Review & Sign-Off and appended to the
 * Test Summary report. Stored base64 so it round-trips through the JSON sync payload. */
export interface PulsationAttachment {
  name: string;
  /** Raw PDF bytes, base64-encoded. */
  base64: string;
  size: number;
  attachedAt: string;
}

/** One row in a per-unit measurement table (a pulsator or a cluster). Values keyed by column. */
export interface MeasurementRow {
  id: string;
  /** Unit / bail number as labelled by the Tester. */
  unit: string;
  values: Record<string, string>;
}

/** One field-level difference recorded when a completed test is re-edited as a new version. */
export interface FieldChange {
  /** Report section the field belongs to (e.g. "Machine configuration"). */
  section: string;
  label: string;
  /** Formatted previous value ("—" when the field was blank). */
  from: string;
  /** Formatted amended value ("—" when the field was cleared). */
  to: string;
}

/** The audit record written when a superseding version is marked complete: what changed vs the
 * version it replaced, when, and by whom. The chain is cumulative — each version carries every
 * prior record — so a single synced test reprints its full amendment history on any device.
 * In-progress edits are deliberately NOT audited; the trail starts once a test is complete. */
export interface AmendmentRecord {
  /** The version this record belongs to (the NEW version). */
  version: number;
  /** When the new version was marked complete (sign-off). */
  amendedAt: string;
  /** The signed-in account that signed off the amendment (login name/email). */
  amendedBy?: string;
  /** The version this one superseded. */
  baseVersion: number;
  baseCompletedAt?: string | null;
  changes: FieldChange[];
  /** True when the superseded version wasn't on-device at sign-off, so no diff could be taken. */
  baseUnavailable?: boolean;
}

/** A Machine Test as held on-device (mirrors the server MachineTest + MachineConfiguration). */
export interface LocalTest {
  /** Client-generated id (used for upsert-by-ClientId on sync). */
  id: string;
  farmId?: string | null;
  farmName: string;
  /** Snapshotted farm details for read-only display in the wizard. */
  farm?: FarmSnapshot;
  config: MachineConfiguration;
  /** Which wizard step the Tester is on (a WizardStep name). */
  currentStep: string;
  /** Visual-fault checklist outcomes, keyed by item key (blank items are absent). */
  visualFaults: Record<string, VisualFaultEntry>;
  /** "Check all as verified" attestations recorded against steps. */
  attestations: ChecklistAttestation[];
  /** Test Record numerical readings, keyed by reading key. */
  readings: Record<string, number>;
  /** Recommendation text per fault, keyed by the fault's source key. */
  recommendations: Record<string, string>;
  /** Visual-fault data-capture fields (belt sizes, diameters, lengths…), keyed by item key. */
  dataFields: Record<string, string>;
  /** Per-pulsator measurement rows (rate / ratio per unit) — ISO 14–15. */
  pulsatorRows?: MeasurementRow[];
  /** Per-cluster measurement rows (air admission / leakage per unit) — ISO 13. */
  clusterRows?: MeasurementRow[];
  /** Pulsation analyser PDF (O3) — appended to the Test Summary report. */
  pulsationPdf?: PulsationAttachment | null;
  /** Visual Faults — Running: guards installed on pulsators. */
  guardsOnPulsators?: boolean;
  /** Snapshot of the TESTER's equipment calibration expiry dates (ISO date strings). These are
   * tester-profile data (see calibrationSync), stamped into the record at sign-off so the
   * printed report reflects equipment state at test time. Superseding versions carry the base
   * version's values; migrated legacy tests carry the dates recorded on the legacy test. */
  calAirFlowMeters?: string | null;
  calPulsatorTesters?: string | null;
  calVacuumGauges?: string | null;
  notes?: string;
  createdAt: string;
  updatedAt: string;
  markedCompleteAt?: string | null;
  syncState: SyncState;
  /** True once the test has ever reached the server (syncState alone can't tell — it flips back
   * to "local-only" as a dirty marker). Tests that exist on the server can't be deleted locally. */
  everUploaded?: boolean;
  /** As-recorded pass/fail per reading key, for MIGRATED legacy tests — shown on the read-only
   * view instead of recomputing (the original verdict at test time). */
  verdicts?: Record<string, "pass" | "fail">;
  /** Migrated legacy test: rendered read-only (a historical record, not editable). */
  readonly?: boolean;
  /** Section-level recommendation narratives as recorded (migrated tests, read-only). */
  recordedRecommendations?: { label: string; text: string }[];
  /** Visual-fault observation texts as recorded (migrated tests, read-only). */
  recordedVisualFaults?: string[];
  /** Version number (1 = original). Bumped when a completed test is reopened as a new version. */
  version?: number;
  /** Client id of the prior version this one supersedes — forms the history chain. Round-trips
   * through PayloadJson, so the server keeps every version as its own linked record. */
  supersedesId?: string;
  /** Cumulative amendment history (one record per superseding version, appended at sign-off).
   * Rendered as the final "Amendment history" page of the Test Summary report. */
  amendments?: AmendmentRecord[];
}

/** A reference-data blob synced from the server (standards, later catalogs), keyed by name. */
export interface ReferenceEntry {
  key: string;
  version?: string | null;
  rows?: unknown;
}

interface AutorepDB extends DBSchema {
  tests: { key: string; value: LocalTest };
  reference: { key: string; value: ReferenceEntry };
}

const DB_PREFIX = "autorep";
const DB_VERSION = 2; // v2: + reference store (synced standards / catalogs)
const LAST_TESTER_KEY = "autorep:lastTesterId";

function currentTesterId(): string | null {
  const id = (globalThis as { __autorepTesterId?: unknown }).__autorepTesterId;
  return typeof id === "string" && id.length > 0 ? id : null;
}

// Per-tester database name so a shared device never exposes one tester's cached tests / farm PII to
// another, and offline-created tests can't be mis-attributed on sync. Falls back to the legacy
// unnamespaced name only when no identity is present (e.g. unit tests).
function dbName(): string {
  const t = currentTesterId();
  return t ? `${DB_PREFIX}_${t}` : DB_PREFIX;
}

function deleteDatabase(name: string): Promise<void> {
  return new Promise((resolve) => {
    const req = indexedDB.deleteDatabase(name);
    req.onsuccess = req.onerror = req.onblocked = () => resolve();
  });
}

export interface PurgeResult {
  /** A previous tester's database was kept because it still holds unsynced tests. */
  retained?: { testerId: string; unsyncedCount: number };
}

/** Unsynced tests in a named database, 0 if it is absent or unreadable. */
async function countUnsyncedIn(name: string): Promise<number> {
  try {
    // No version argument: open whatever is there rather than triggering an upgrade.
    const database = await openDB<AutorepDB>(name);
    try {
      if (!database.objectStoreNames.contains("tests")) return 0;
      const all = await database.getAll("tests");
      return all.filter((t) => t?.syncState === "local-only").length;
    } finally {
      database.close();
    }
  } catch {
    return 0;
  }
}

/** Deletes a previous tester's local database when a different tester signs in on this device, so
 * cached tests + farm PII never leak across accounts. Call once at startup before any store
 * access. Best-effort — never blocks app start (per-tester DB naming is the primary isolation).
 *
 * Two things it deliberately will NOT do:
 *  - Purge when the current tester is unknown. Identity arrives from the server, so "unknown"
 *    means "not established yet", not "nobody" — deleting on that basis would wipe a tester's
 *    queued work every time the page rendered without it.
 *  - Delete a database holding unsynced tests. That work exists nowhere else, so it outranks the
 *    cache hygiene this function exists for; the outgoing tester's cached data stays on disk
 *    under a name the incoming session never opens. The caller is told so it can warn someone. */
export async function purgeStaleLocalData(): Promise<PurgeResult> {
  try {
    const current = currentTesterId();
    if (current === null) return {};
    const last = localStorage.getItem(LAST_TESTER_KEY) ?? "";
    if (last === current) return {};

    await deleteDatabase(DB_PREFIX); // drop the legacy shared DB if it exists
    if (last) {
      const unsyncedCount = await countUnsyncedIn(`${DB_PREFIX}_${last}`);
      if (unsyncedCount > 0) {
        // Leave LAST_TESTER_KEY pointing at the outgoing tester so the check runs again next load
        // and the database is cleaned up once that work has synced.
        return { retained: { testerId: last, unsyncedCount } };
      }
      await deleteDatabase(`${DB_PREFIX}_${last}`);
    }
    localStorage.setItem(LAST_TESTER_KEY, current);
    return {};
  } catch {
    return {};
  }
}

let dbPromise: Promise<IDBPDatabase<AutorepDB>> | null = null;

function db(): Promise<IDBPDatabase<AutorepDB>> {
  dbPromise ??= openDB<AutorepDB>(dbName(), DB_VERSION, {
    upgrade(database) {
      if (!database.objectStoreNames.contains("tests")) {
        database.createObjectStore("tests", { keyPath: "id" });
      }
      if (!database.objectStoreNames.contains("reference")) {
        database.createObjectStore("reference", { keyPath: "key" });
      }
    },
  });
  return dbPromise;
}

export async function getReference(key: string): Promise<ReferenceEntry | undefined> {
  return (await db()).get("reference", key);
}

export async function putReference(entry: ReferenceEntry): Promise<void> {
  await (await db()).put("reference", entry);
}

export async function getTest(id: string): Promise<LocalTest | undefined> {
  return (await db()).get("tests", id);
}

export async function putTest(test: LocalTest): Promise<void> {
  await (await db()).put("tests", test);
}

export async function allTests(): Promise<LocalTest[]> {
  return (await db()).getAll("tests");
}

export async function deleteTest(id: string): Promise<void> {
  await (await db()).delete("tests", id);
}
