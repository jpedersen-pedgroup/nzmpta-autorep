// Fetches a farm's details for the wizard to snapshot. Online-only (best-effort); offline
// resumes use the snapshot already stored on the test.
import type { FarmSnapshot } from "./db/testStore";

export async function fetchFarm(id: string): Promise<FarmSnapshot | null> {
  try {
    const res = await fetch(`/api/farms/${id}`, { headers: { Accept: "application/json" } });
    if (!res.ok) return null;
    const f = (await res.json()) as FarmSnapshot & { id: string };
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
  } catch {
    return null;
  }
}
