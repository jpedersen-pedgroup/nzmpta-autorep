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

export async function fetchFarm(id: string): Promise<FarmSnapshot | null> {
  try {
    const res = await fetch(`/api/farms/${id}`, { headers: { Accept: "application/json" } });
    // A definitive server answer stands: 404 means out-of-scope/inactive — don't resurrect it
    // from the cache. Only an unreachable server falls back to the cached farm book.
    if (!res.ok) return null;
    return toSnapshot((await res.json()) as FarmSnapshot & { id: string });
  } catch {
    const cached = await getCachedFarm(id);
    return cached ? toSnapshot(cached) : null;
  }
}
