// Syncs the admin-managed fault-observation catalog (wording + severity + recommendation per
// visual check) to the device — cached-then-fresh, bundled defaults as the offline fallback.
import { getReference, putReference } from "../db/testStore";
import { applyFaultCatalogOverrides, type FaultObservationDto } from "../reference/faultCatalog";
import { fetchWithTimeout } from "../connectivity";

const REF_KEY = "faultObservations";

interface FaultCatalogResponse {
  version: string | null;
  items: FaultObservationDto[];
}

export async function initFaultCatalog(): Promise<void> {
  await applyCachedFaultCatalog();
  await refreshFaultCatalog();
}

/** IndexedDB only — never waits on the network. */
export async function applyCachedFaultCatalog(): Promise<void> {
  try {
    const cached = await getReference(REF_KEY);
    if (cached?.rows) applyFaultCatalogOverrides(cached.rows as FaultObservationDto[]);
  } catch {
    // IndexedDB unavailable — bundled catalog stays in effect.
  }
}

/** True when the server's catalog differed from the cached one and is now in effect. */
export async function refreshFaultCatalog(): Promise<boolean> {
  try {
    const cachedVersion = await getReference(REF_KEY).then((c) => c?.version, () => undefined);
    const res = await fetchWithTimeout("/api/fault-observations", { headers: { Accept: "application/json" } });
    if (!res.ok) return false;
    const data = (await res.json()) as FaultCatalogResponse;
    if (!data.version || !Array.isArray(data.items)) return false;
    applyFaultCatalogOverrides(data.items);
    await putReference({ key: REF_KEY, version: data.version, rows: data.items });
    return data.version !== cachedVersion;
  } catch {
    // Offline — cached or bundled catalog stays in effect.
    return false;
  }
}
