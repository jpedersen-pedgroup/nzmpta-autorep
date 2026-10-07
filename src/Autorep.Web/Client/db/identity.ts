// Who last signed in on this device — the record the offline shell runs on.
//
// Identity only ever reaches the client from the server: a server-rendered page carries it in
// window.__autorepTesterId, and GET /api/session returns it in full. The shell document the
// service worker serves offline is static and shared by every account on the device, so it carries
// no identity at all. This record is how a cold launch with no signal knows whose tests to open,
// and what name, initials and licence banner to draw.
//
// Where it lives, and why:
//  - Not the Cache API. That is shared, and its contents are visible to anyone who opens the
//    device's storage — the shell strategy rests on nothing identifying landing there.
//  - Not localStorage (decided, plans/offline-tester-app.md §5a).
//  - Not the per-tester database. Its NAME is the tester id (autorep_<id>), so reading the id from
//    inside it would be circular. It gets a small database of its own instead, deliberately not
//    named autorep_<something>: purgeStaleLocalData treats every autorep_* database as a tester's
//    store and would delete this one.
//
// "Offline = one tester per device" follows from this: signing in is a server POST, so the record
// can only be written or replaced online, and signing out clears it (wwwroot/js/pwa-register.js).
// It is a routing hint, not a credential — the server still decides everything on every request.
import { openDB } from "idb";

export interface IdentityRecord {
  testerId: string;
  displayName: string;
  /** The login name (email) — what amendment records name as "amended by". */
  userName: string | null;
  roles: string[];
  certificateNo: string | null;
  /** Tester licence expiry, ISO yyyy-mm-dd; null when never set. */
  licenceExpiryDate: string | null;
  /** A lapsed licence's session: may only send work already on the device. */
  syncOnly: boolean;
  /** When the server last confirmed this record (device clock). */
  confirmedAt: string;
}

export const IDENTITY_DB = "autorep-identity";
const STORE = "identity";
const KEY = "current";

let current: IdentityRecord | null = null;

/** The identity read by the last loadIdentity()/saveIdentity() call, synchronously. */
export function cachedIdentity(): IdentityRecord | null {
  return current;
}

function open() {
  return openDB(IDENTITY_DB, 1, {
    upgrade(db) {
      if (!db.objectStoreNames.contains(STORE)) db.createObjectStore(STORE);
    },
  });
}

/** Reads the record into memory. Null when nobody has signed in here, or the read failed. Every
 * connection is closed straight after use: an open one would block the sign-out's delete. */
export async function loadIdentity(): Promise<IdentityRecord | null> {
  try {
    const db = await open();
    try {
      current = normaliseIdentity(await db.get(STORE, KEY));
    } finally {
      db.close();
    }
  } catch {
    current = null;
  }
  return current;
}

export async function saveIdentity(record: IdentityRecord): Promise<void> {
  current = record;
  try {
    const db = await open();
    try {
      await db.put(STORE, record, KEY);
    } finally {
      db.close();
    }
  } catch {
    // Storage refused — this session still has it in memory; the next online load retries.
  }
}

export async function clearIdentity(): Promise<void> {
  current = null;
  await new Promise<void>((resolve) => {
    const req = indexedDB.deleteDatabase(IDENTITY_DB);
    req.onsuccess = req.onerror = req.onblocked = () => resolve();
  });
}

const str = (v: unknown): string | null => (typeof v === "string" && v.trim() !== "" ? v.trim() : null);

/** Validates a stored record (or anything else) into an IdentityRecord, or null. */
export function normaliseIdentity(value: unknown): IdentityRecord | null {
  const v = (value ?? {}) as Record<string, unknown>;
  const testerId = str(v.testerId);
  if (!testerId) return null;
  return {
    testerId,
    displayName: str(v.displayName) ?? str(v.userName) ?? "",
    userName: str(v.userName),
    roles: Array.isArray(v.roles) ? v.roles.filter((r): r is string => typeof r === "string") : [],
    certificateNo: str(v.certificateNo),
    licenceExpiryDate: /^\d{4}-\d{2}-\d{2}$/.test(String(v.licenceExpiryDate ?? "")) ? String(v.licenceExpiryDate) : null,
    syncOnly: v.syncOnly === true,
    confirmedAt: str(v.confirmedAt) ?? new Date(0).toISOString(),
  };
}

/** A GET /api/session body as an IdentityRecord confirmed now, or null when it isn't one. */
export function identityFromSession(dto: unknown, now = new Date()): IdentityRecord | null {
  const record = normaliseIdentity(dto);
  return record ? { ...record, confirmedAt: now.toISOString() } : null;
}

export const TESTER_ROLE = "Tester";
const ADMIN_ROLES = ["SuperAdministrator", "CompanyAdministrator"];

/** Where `/` sends this identity — the client-side twin of Pages/Index.cshtml.cs, for a cold launch
 * the server can't answer. Admins are online-only, so they get no offline destination. */
export function homeFor(identity: IdentityRecord | null): "tester" | "sync-only" | "admin" | "nobody" {
  if (!identity) return "nobody";
  if (identity.roles.some((r) => ADMIN_ROLES.includes(r))) {
    return identity.roles.includes(TESTER_ROLE) ? "tester" : "admin";
  }
  if (!identity.roles.includes(TESTER_ROLE)) return "nobody";
  return identity.syncOnly ? "sync-only" : "tester";
}
