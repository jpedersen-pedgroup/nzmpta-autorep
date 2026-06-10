// Syncs the admin-managed test standards to the device. On app start: apply the cached set from
// IndexedDB (so offline uses the last-synced values), then fetch /api/standards — if the server
// version differs from what this device last saw, apply + cache the new set and tell the Tester.
import { getReference, putReference } from "../db/testStore";
import { applyStandardsOverrides, type StandardDto } from "../passfail/standardsOverrides";
import { showToast } from "../ui/toast";

const REF_KEY = "standards";

interface StandardsResponse {
  version: string | null;
  standards: StandardDto[];
}

export async function initStandards(): Promise<void> {
  let cachedVersion: string | null | undefined;
  try {
    const cached = await getReference(REF_KEY);
    if (cached?.rows) {
      applyStandardsOverrides(cached.rows as StandardDto[]);
      cachedVersion = cached.version;
    }
  } catch {
    // IndexedDB unavailable — built-in defaults stay in effect.
  }

  try {
    const res = await fetch("/api/standards", { headers: { Accept: "application/json" } });
    if (!res.ok) return; // offline / unauthenticated — cached or built-in values stay in effect
    const data = (await res.json()) as StandardsResponse;
    if (!data.version || !Array.isArray(data.standards)) return;

    applyStandardsOverrides(data.standards);
    await putReference({ key: REF_KEY, version: data.version, rows: data.standards });

    if (cachedVersion && data.version > cachedVersion) {
      showToast(
        "Test standards have been updated since your last sync — the new limits are now in effect.",
        "info",
        9000,
      );
    }
  } catch {
    // Offline — cached or built-in values stay in effect.
  }
}
