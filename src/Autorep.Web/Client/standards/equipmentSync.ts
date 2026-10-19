// Syncs the admin-managed OEM Specs (equipment catalogs and their specs) to the device: apply the
// cached set from IndexedDB (offline-first), then fetch /api/equipment and cache the fresh set. No
// tester notice — catalog additions and spec edits are not a compliance change the way standards are,
// and a test already signed off keeps the specs it was judged against (reference/oemSpecs.ts).
import { getReference, putReference } from "../db/testStore";
import { applyEquipmentOverrides, type EquipmentDto } from "../reference/catalogOverrides";
import { fetchWithTimeout } from "../connectivity";

const REF_KEY = "equipment";

interface EquipmentResponse {
  version: string | null;
  items: EquipmentDto[];
  /** Deactivated items, for their specs (absent from a server that predates specs). */
  inactive?: EquipmentDto[];
}

/** What the cache holds: the active items followed by the deactivated ones marked `active: false`.
 * A set cached before specs existed is the active items alone, with no `spec` on any of them. */
type CachedRows = EquipmentDto[];

export async function initEquipment(): Promise<void> {
  await applyCachedEquipment();
  await refreshEquipment();
}

/** IndexedDB only — never waits on the network. */
export async function applyCachedEquipment(): Promise<void> {
  try {
    const cached = await getReference(REF_KEY);
    if (Array.isArray(cached?.rows)) {
      const rows = cached.rows as CachedRows;
      applyEquipmentOverrides(rows.filter((r) => r.active !== false), rows.filter((r) => r.active === false));
    }
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
    const inactive = Array.isArray(data.inactive) ? data.inactive : [];
    applyEquipmentOverrides(data.items, inactive);
    const rows: CachedRows = [...data.items, ...inactive.map((i) => ({ ...i, active: false }))];
    await putReference({ key: REF_KEY, version: data.version, rows });
    return data.version !== cachedVersion;
  } catch {
    // Offline — cached or bundled catalogs stay in effect.
    return false;
  }
}
