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
  /** Visual Faults — Running: guards installed on pulsators. */
  guardsOnPulsators?: boolean;
  /** Tester equipment calibration expiry dates (ISO date strings), shown with the farm details. */
  calAirFlowMeters?: string | null;
  calPulsatorTesters?: string | null;
  calVacuumGauges?: string | null;
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
