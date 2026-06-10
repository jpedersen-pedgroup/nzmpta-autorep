// Syncs the admin-managed fault-observation catalog (wording + severity + recommendation per
// visual check) to the device — cached-then-fresh, bundled defaults as the offline fallback.
import { getReference, putReference } from "../db/testStore";
import { applyFaultCatalogOverrides, type FaultObservationDto } from "../reference/faultCatalog";

const REF_KEY = "faultObservations";

interface FaultCatalogResponse {
  version: string | null;
  items: FaultObservationDto[];
}

export async function initFaultCatalog(): Promise<void> {
  try {
    const cached = await getReference(REF_KEY);
    if (cached?.rows) applyFaultCatalogOverrides(cached.rows as FaultObservationDto[]);
  } catch {
    // IndexedDB unavailable — bundled catalog stays in effect.
  }

  try {
    const res = await fetch("/api/fault-observations", { headers: { Accept: "application/json" } });
    if (!res.ok) return;
    const data = (await res.json()) as FaultCatalogResponse;
    if (!data.version || !Array.isArray(data.items)) return;
    applyFaultCatalogOverrides(data.items);
    await putReference({ key: REF_KEY, version: data.version, rows: data.items });
  } catch {
    // Offline — cached or bundled catalog stays in effect.
  }
}
