// Caches the tester's Testing Company branding (name + logo) from /api/profile/company so the
// report letterhead carries the company logo when generated offline on-farm. Read-only on the
// device: admins change the logo in the portal, and the device picks it up on the next app load
// or sync. The logo can be up to 1 MB, so the cached ETag rides along as If-None-Match and an
// unchanged logo costs a bodyless 304 rather than a re-download.
import { getReference, putReference } from "../db/testStore";

export interface CompanyBranding {
  id: string;
  name: string;
  /** Data URL (`data:image/png;base64,…`), or null when the company has no logo. */
  logo: string | null;
}

interface CachedCompanyBranding {
  branding: CompanyBranding | null;
  etag?: string | null;
}

const REF_KEY = "testerCompany";

async function readCache(): Promise<CachedCompanyBranding | null> {
  try {
    const rows = (await getReference(REF_KEY))?.rows as CachedCompanyBranding | undefined;
    return rows && typeof rows === "object" && "branding" in rows ? rows : null;
  } catch {
    return null;
  }
}

function normalize(dto: unknown): CompanyBranding | null {
  const d = (dto ?? {}) as Record<string, unknown>;
  if (typeof d.id !== "string" || typeof d.name !== "string") return null;
  return { id: d.id, name: d.name, logo: typeof d.logo === "string" && d.logo.startsWith("data:") ? d.logo : null };
}

/** Pull the tester's company branding (tester pages, on app load and each sync). Offline or any
 * error leaves the cache as it was, so the last-known logo keeps printing. */
export async function initCompanyBranding(): Promise<void> {
  try {
    const cached = await readCache();
    const headers: Record<string, string> = { Accept: "application/json" };
    if (cached?.etag) headers["If-None-Match"] = cached.etag;
    const res = await fetch("/api/profile/company", { headers });
    if (res.status === 304) return;
    // No company any more: stop printing the old one's letterhead.
    if (res.status === 204) {
      await putReference({ key: REF_KEY, rows: { branding: null, etag: null } satisfies CachedCompanyBranding });
      return;
    }
    if (!res.ok) return;
    await putReference({
      key: REF_KEY,
      rows: { branding: normalize(await res.json()), etag: res.headers.get("ETag") } satisfies CachedCompanyBranding,
    });
  } catch {
    // Offline — the cached branding stays in effect.
  }
}

/** The last-synced company branding; null when never synced or the tester has no company. */
export async function getCachedCompanyBranding(): Promise<CompanyBranding | null> {
  return (await readCache())?.branding ?? null;
}
