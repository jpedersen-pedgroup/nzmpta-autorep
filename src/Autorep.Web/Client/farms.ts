// Fetches a farm's details for the wizard to snapshot. Network-first; when offline (or the
// fetch fails) falls back to the farm book cached on-device at app load, so a test started
// offline still gets its farm details. Offline resumes use the snapshot already on the test.
import type { FarmSnapshot } from "./db/testStore";
import { getCachedFarm, type CachedFarm } from "./sync/farmsSync";

function toSnapshot(f: CachedFarm | (FarmSnapshot & { id: string })): FarmSnapshot {
  return {
    name: f.name,
    supplyNumber: f.supplyNumber,
    addressLine1: f.addressLine1,
    addressLine2: f.addressLine2,
    town: f.town,
    postCode: f.postCode,
    rapidNumber: f.rapidNumber,
    regionName: f.regionName,
    milkCompanyName: f.milkCompanyName,
    farmerName: f.farmerName,
    contactPhone: f.contactPhone,
    contactEmail: f.contactEmail,
  };
}

/** Matches connectivity.ts — a hung request on weak rural signal must not stall the wizard's
 * first render, which awaits this call before showing anything. */
const TIMEOUT_MS = 5_000;

async function cached(id: string): Promise<FarmSnapshot | null> {
  const hit = await getCachedFarm(id);
  return hit ? toSnapshot(hit) : null;
}

export async function fetchFarm(id: string): Promise<FarmSnapshot | null> {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), TIMEOUT_MS);
  try {
    const res = await fetch(`/api/farms/${id}`, {
      headers: { Accept: "application/json" },
      signal: controller.signal,
      redirect: "manual",
    });
    // A 404 is a definitive answer — out-of-scope or inactive — so don't resurrect the farm from
    // the cache. Anything else (signed out, server error, an opaque redirect) says nothing about
    // this farm, so prefer the cached book over stranding a test with no farm details.
    if (res.status === 404) return null;
    if (!res.ok || res.type === "opaqueredirect" || res.redirected) return await cached(id);
    return toSnapshot((await res.json()) as FarmSnapshot & { id: string });
  } catch {
    // Offline, DNS failure, or the timeout above.
    return await cached(id);
  } finally {
    clearTimeout(timer);
  }
}
