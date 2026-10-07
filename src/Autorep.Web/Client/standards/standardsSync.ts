// Syncs the admin-managed test standards to the device. On app start: apply the cached set from
// IndexedDB (so offline uses the last-synced values), then fetch /api/standards — if the server
// version differs from what this device last saw, apply + cache the new set and tell the Tester.
// The two halves are separate so the page can mount on the cached set without waiting on the
// network (Client/main.ts); initStandards runs both.
import { getReference, putReference } from "../db/testStore";
import { applyStandardsOverrides, type StandardDto } from "../passfail/standardsOverrides";
import { showToast } from "../ui/toast";
import { fetchWithTimeout } from "../connectivity";

const REF_KEY = "standards";

interface StandardsResponse {
  version: string | null;
  standards: StandardDto[];
}

export async function initStandards(): Promise<void> {
  await applyCachedStandards();
  await refreshStandards();
}

/** IndexedDB only — never waits on the network. */
export async function applyCachedStandards(): Promise<void> {
  try {
    const cached = await getReference(REF_KEY);
    if (cached?.rows) applyStandardsOverrides(cached.rows as StandardDto[]);
  } catch {
    // IndexedDB unavailable — built-in defaults stay in effect.
  }
}

/** Fetches the current set; true when it differed from the cached one and is now in effect. */
export async function refreshStandards(): Promise<boolean> {
  try {
    const cachedVersion = await getReference(REF_KEY).then((c) => c?.version, () => undefined);
    const res = await fetchWithTimeout("/api/standards", { headers: { Accept: "application/json" } });
    if (!res.ok) return false; // offline / unauthenticated — cached or built-in values stay in effect
    const data = (await res.json()) as StandardsResponse;
    if (!data.version || !Array.isArray(data.standards)) return false;

    applyStandardsOverrides(data.standards);
    await putReference({ key: REF_KEY, version: data.version, rows: data.standards });

    if (cachedVersion && data.version > cachedVersion) {
      showToast(
        "Test standards have been updated since your last sync — the new limits are now in effect.",
        "info",
        9000,
      );
    }
    return data.version !== cachedVersion;
  } catch {
    // Offline — cached or built-in values stay in effect.
    return false;
  }
}
