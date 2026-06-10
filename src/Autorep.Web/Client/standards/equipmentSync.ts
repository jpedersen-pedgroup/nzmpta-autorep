// Syncs the admin-managed equipment catalogs to the device: apply the cached set from IndexedDB
// (offline-first), then fetch /api/equipment and cache the fresh set. No tester notice — catalog
// additions are not a compliance change the way standards are.
import { getReference, putReference } from "../db/testStore";
import { applyEquipmentOverrides, type EquipmentDto } from "../reference/catalogOverrides";

const REF_KEY = "equipment";

interface EquipmentResponse {
  version: string | null;
  items: EquipmentDto[];
}

export async function initEquipment(): Promise<void> {
  try {
    const cached = await getReference(REF_KEY);
    if (cached?.rows) applyEquipmentOverrides(cached.rows as EquipmentDto[]);
  } catch {
    // IndexedDB unavailable — bundled catalogs stay in effect.
  }

  try {
    const res = await fetch("/api/equipment", { headers: { Accept: "application/json" } });
    if (!res.ok) return;
    const data = (await res.json()) as EquipmentResponse;
    if (!data.version || !Array.isArray(data.items)) return;
    applyEquipmentOverrides(data.items);
    await putReference({ key: REF_KEY, version: data.version, rows: data.items });
  } catch {
    // Offline — cached or bundled catalogs stay in effect.
  }
}
