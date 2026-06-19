// Syncs the admin-managed privacy content to the device (cache-then-fresh, mirroring standardsSync)
// so the report footer + in-app collection notice work offline.
import { getReference, putReference } from "../db/testStore";
import { applyPrivacyContent, type PrivacyContentData } from "../config/privacyContent";

const REF_KEY = "privacy";

interface PrivacyResponse {
  version: string | null;
  content: PrivacyContentData | null;
}

export async function initPrivacy(): Promise<void> {
  try {
    const cached = await getReference(REF_KEY);
    if (cached?.rows) applyPrivacyContent(cached.rows as PrivacyContentData);
  } catch {
    // IndexedDB unavailable — the built-in default footer stays in effect.
  }

  try {
    const res = await fetch("/api/privacy", { headers: { Accept: "application/json" } });
    if (!res.ok) return; // offline / unauthenticated — cached or built-in values stay in effect
    const data = (await res.json()) as PrivacyResponse;
    if (!data.content) return;

    applyPrivacyContent(data.content);
    await putReference({ key: REF_KEY, version: data.version, rows: data.content });
  } catch {
    // Offline — cached or built-in values stay in effect.
  }
}
