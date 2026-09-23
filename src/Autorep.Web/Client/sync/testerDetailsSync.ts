// Caches the signed-in tester's own details from /api/profile/tester (name, phone, NZMPTA
// registration number and expiry) so the report can name who did the test, and give the farmer a
// number to call, when it's generated offline on-farm. Read-only on the device: an admin maintains
// them. Refreshed on app load and each sync; offline, the last-known details stay in effect.
import { getReference, putReference, type TesterDetails } from "../db/testStore";

export type { TesterDetails };

const REF_KEY = "testerDetails";

function normalize(dto: unknown): TesterDetails | null {
  const d = (dto ?? {}) as Record<string, unknown>;
  const str = (v: unknown) => (typeof v === "string" && v.trim() !== "" ? v.trim() : null);
  const name = str(d.name);
  if (!name) return null;
  return { name, phone: str(d.phone), registrationNumber: str(d.registrationNumber), registrationExpiry: str(d.registrationExpiry) };
}

export async function initTesterDetails(): Promise<void> {
  try {
    const res = await fetch("/api/profile/tester", { headers: { Accept: "application/json" } });
    if (!res.ok) return;
    const details = normalize(await res.json());
    if (details) await putReference({ key: REF_KEY, rows: details });
  } catch {
    // Offline — the cached details stay in effect.
  }
}

/** The last-synced details; null when never synced. */
export async function getCachedTesterDetails(): Promise<TesterDetails | null> {
  try {
    return normalize((await getReference(REF_KEY))?.rows);
  } catch {
    return null;
  }
}
