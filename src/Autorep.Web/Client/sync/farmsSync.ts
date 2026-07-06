// Syncs the caller's farm book (company-scoped, from /api/farms) to the device on app start,
// mirroring the cache-then-fresh reference-data pattern (standardsSync et al). The cache lets the
// wizard resolve a farm's details while offline — including farms the office added since the
// device last loaded the app. Full replace on every successful fetch: farms deactivated or
// re-scoped since the last visit drop out of the cache rather than lingering.
import { getReference, putReference } from "../db/testStore";

/** A farm as cached on-device (mirrors the server FarmDto). */
export interface CachedFarm {
  id: string;
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
  milkCompanyId?: string | null;
}

const REF_KEY = "farms";

export async function initFarms(): Promise<void> {
  try {
    const res = await fetch("/api/farms", { headers: { Accept: "application/json" } });
    if (!res.ok) return; // offline / unauthenticated — the cached farm book stays in effect
    const farms = (await res.json()) as CachedFarm[];
    if (!Array.isArray(farms)) return;
    await putReference({ key: REF_KEY, rows: farms });
  } catch {
    // Offline — the cached farm book stays in effect.
  }
}

/** The last-synced farm book (empty when never synced on this device). */
export async function getCachedFarms(): Promise<CachedFarm[]> {
  try {
    const cached = await getReference(REF_KEY);
    return Array.isArray(cached?.rows) ? (cached.rows as CachedFarm[]) : [];
  } catch {
    return [];
  }
}

/** Offline farm lookup by id against the cached farm book. */
export async function getCachedFarm(id: string): Promise<CachedFarm | undefined> {
  return (await getCachedFarms()).find((f) => f.id === id);
}
