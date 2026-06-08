// IndexedDB store for the offline Tester PWA. For now it holds the Tester's Machine Tests
// (created/edited on-device); later phases add reference-data, vendor-spec snapshots and cached
// Final Report blobs. Wraps `idb` for a small typed surface.
import { openDB, type DBSchema, type IDBPDatabase } from "idb";
import type { ChecklistAttestation, MachineConfiguration, VisualFaultEntry } from "../wizard/types";

export type SyncState = "local-only" | "uploading" | "uploaded" | "merge-conflict";

/** A Machine Test as held on-device (mirrors the server MachineTest + MachineConfiguration). */
export interface LocalTest {
  /** Client-generated id (used for upsert-by-ClientId on sync). */
  id: string;
  farmId?: string | null;
  farmName: string;
  config: MachineConfiguration;
  /** Which wizard step the Tester is on (a WizardStep name). */
  currentStep: string;
  /** Visual-fault checklist outcomes, keyed by item key (blank items are absent). */
  visualFaults: Record<string, VisualFaultEntry>;
  /** "Check all as verified" attestations recorded against steps. */
  attestations: ChecklistAttestation[];
  /** Visual Faults — Running: guards installed on pulsators. */
  guardsOnPulsators?: boolean;
  notes?: string;
  createdAt: string;
  updatedAt: string;
  markedCompleteAt?: string | null;
  syncState: SyncState;
}

interface AutorepDB extends DBSchema {
  tests: { key: string; value: LocalTest };
}

const DB_NAME = "autorep";
const DB_VERSION = 1;

let dbPromise: Promise<IDBPDatabase<AutorepDB>> | null = null;

function db(): Promise<IDBPDatabase<AutorepDB>> {
  dbPromise ??= openDB<AutorepDB>(DB_NAME, DB_VERSION, {
    upgrade(database) {
      if (!database.objectStoreNames.contains("tests")) {
        database.createObjectStore("tests", { keyPath: "id" });
      }
    },
  });
  return dbPromise;
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
