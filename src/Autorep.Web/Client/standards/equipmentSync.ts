// Syncs the admin-managed equipment catalogs to the device: apply the cached set from IndexedDB
// (offline-first), then fetch /api/equipment and cache the fresh set. No tester notice — catalog
// additions are not a compliance change the way standards are.
import { getReference, putReference } from "../db/testStore";
import { applyEquipmentOverrides, type EquipmentDto } from "../reference/catalogOverrides";
import { fetchWithTimeout } from "../connectivity";

const REF_KEY = "equipment";

interface EquipmentResponse {
  version: string | null;
  items: EquipmentDto[];
}

export async function initEquipment(): Promise<void> {
  await applyCachedEquipment();
  await refreshEquipment();
}

/** IndexedDB only — never waits on the network. */
export async function applyCachedEquipment(): Promise<void> {
  try {
    const cached = await getReference(REF_KEY);
    if (cached?.rows) applyEquipmentOverrides(cached.rows as EquipmentDto[]);
  } catch {
    // IndexedDB unavailable — bundled catalogs stay in effect.
  }
}

/** True when the server's set differed from the cached one and is now in effect. */
export async function refreshEquipment(): Promise<boolean> {
  try {
    const cachedVersion = await getReference(REF_KEY).then((c) => c?.version, () => undefined);
    const res = await fetchWithTimeout("/api/equipment", { headers: { Accept: "application/json" } });
    if (!res.ok) return false;
    const data = (await res.json()) as EquipmentResponse;
    if (!data.version || !Array.isArray(data.items)) return false;
    applyEquipmentOverrides(data.items);
    await putReference({ key: REF_KEY, version: data.version, rows: data.items });
    return data.version !== cachedVersion;
  } catch {
    // Offline — cached or bundled catalogs stay in effect.
    return false;
  }
}
