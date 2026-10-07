// Syncs the caller's farm book (company-scoped, from /api/farms) to the device on every tester
// page, mirroring the cache-then-fresh reference-data pattern (standardsSync et al). The cache is
// what the New-test picker offers and what the wizard snapshots a farm from while offline —
// including farms the office added since the device last loaded the app. Full replace on every
// changed answer: farms deactivated or re-scoped since the last visit drop out of the cache rather
// than lingering.
//
// The book is the largest thing a tester page fetches, so it is fetched conditionally: the server
// stamps an ETag over the exact bytes, the device keeps it with the rows, and an unchanged book
// costs a bodyless 304.
import { getReference, putReference } from "../db/testStore";
import { fetchWithTimeout } from "../connectivity";

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

/** A milk-supply company's logo. The service worker keeps every one it fetches (LOGO_CACHE). */
export function milkCompanyLogoUrl(milkCompanyId: string): string {
  return `/api/milk-companies/${milkCompanyId}/logo`;
}

/** Refreshes the cached farm book. True when it changed (and is now in effect). Offline, signed
 * out or any failure leaves the cached book exactly as it was. */
export async function initFarms(): Promise<boolean> {
  try {
    const cached = await getReference(REF_KEY).catch(() => undefined);
    const headers: Record<string, string> = { Accept: "application/json" };
    if (cached?.version && Array.isArray(cached.rows)) headers["If-None-Match"] = cached.version;

    const res = await fetchWithTimeout("/api/farms", { headers });
    if (res.status === 304) {
      if (firstWarmThisSession()) void warmMilkCompanyLogos(cached?.rows as CachedFarm[]);
      return false;
    }
    if (!res.ok) return false; // offline / unauthenticated — the cached farm book stays in effect
    const farms = (await res.json()) as CachedFarm[];
    if (!Array.isArray(farms)) return false;
    await putReference({ key: REF_KEY, version: res.headers.get("ETag"), rows: farms });
    void warmMilkCompanyLogos(farms);
    return true;
  } catch {
    // Offline — the cached farm book stays in effect.
    return false;
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

/** Puts a farm the tester has just added into the cached book, so it can be picked offline from
 * now on without waiting for the next sync. The stored ETag is left alone: the server's book has
 * changed, so the next refresh gets a full answer and replaces this one anyway. */
export async function addFarmToCache(farm: CachedFarm): Promise<void> {
  const cached = await getReference(REF_KEY).catch(() => undefined);
  const rows = Array.isArray(cached?.rows) ? (cached.rows as CachedFarm[]) : [];
  const next = [...rows.filter((f) => f.id !== farm.id), farm].sort((a, b) => a.name.localeCompare(b.name));
  await putReference({ key: REF_KEY, version: cached?.version ?? null, rows: next });
  void warmMilkCompanyLogos([farm]);
}

const LOGOS_WARMED_KEY = "autorep:logos-warmed";

/** True the first time it's asked in this tab — so an unchanged book still re-checks its logos
 * once a session (a capped or evicted logo cache gets topped up), not on every page. */
function firstWarmThisSession(): boolean {
  try {
    if (sessionStorage.getItem(LOGOS_WARMED_KEY)) return false;
    sessionStorage.setItem(LOGOS_WARMED_KEY, "1");
    return true;
  } catch {
    return false;
  }
}

/**
 * Pre-caches the logo of every milk-supply company in the farm book, so the New-test picker shows
 * one offline for a farm the tester has never chosen before. The service worker's logo rule keeps
 * whatever comes back; this only asks for the ones the device doesn't hold yet. Fire and forget —
 * nobody waits on a logo.
 */
export async function warmMilkCompanyLogos(farms: readonly CachedFarm[] | null | undefined): Promise<number> {
  if (!Array.isArray(farms) || typeof caches === "undefined") return 0;
  const ids = [...new Set(farms.map((f) => f.milkCompanyId).filter((id): id is string => !!id))];
  let fetched = 0;
  for (const id of ids) {
    try {
      const url = milkCompanyLogoUrl(id);
      if (await caches.match(url)) continue;
      const res = await fetchWithTimeout(url);
      if (res.ok) fetched++;
    } catch {
      // Offline part-way through — the rest wait for the next sync.
      break;
    }
  }
  return fetched;
}
