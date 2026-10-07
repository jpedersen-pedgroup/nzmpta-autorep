// Syncs the admin-managed privacy content to the device (cache-then-fresh, mirroring standardsSync)
// so the report footer + in-app collection notice work offline.
import { getReference, putReference } from "../db/testStore";
import { applyPrivacyContent, type PrivacyContentData } from "../config/privacyContent";
import { fetchWithTimeout } from "../connectivity";

const REF_KEY = "privacy";

interface PrivacyResponse {
  version: string | null;
  content: PrivacyContentData | null;
}

export async function initPrivacy(): Promise<void> {
  await applyCachedPrivacy();
  await refreshPrivacy();
}

/** IndexedDB only — never waits on the network. */
export async function applyCachedPrivacy(): Promise<void> {
  try {
    const cached = await getReference(REF_KEY);
    if (cached?.rows) applyPrivacyContent(cached.rows as PrivacyContentData);
  } catch {
    // IndexedDB unavailable — the built-in default footer stays in effect.
  }
}

/** True when the server's content differed from the cached copy and is now in effect. */
export async function refreshPrivacy(): Promise<boolean> {
  try {
    const cachedVersion = await getReference(REF_KEY).then((c) => c?.version, () => undefined);
    const res = await fetchWithTimeout("/api/privacy", { headers: { Accept: "application/json" } });
    if (!res.ok) return false; // offline / unauthenticated — cached or built-in values stay in effect
    const data = (await res.json()) as PrivacyResponse;
    if (!data.content) return false;

    applyPrivacyContent(data.content);
    await putReference({ key: REF_KEY, version: data.version, rows: data.content });
    return data.version !== cachedVersion;
  } catch {
    // Offline — cached or built-in values stay in effect.
    return false;
  }
}
