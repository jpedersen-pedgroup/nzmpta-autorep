// Testing-company report logos, held on-device so the Test Summary can print one offline in the
// shed. Each logo is stored as a data URL in the reference store (the per-tester IndexedDB), not
// left to the service worker's runtime cache: the report may be printed for a test whose logo was
// never displayed online, and pdfmake wants the bytes inline anyway.
//
// Refreshed on app load and after every sync with a conditional GET — the server's ETag is a hash
// of the logo bytes, so an unchanged logo costs a 304, and a replaced or removed one reaches the
// device on its next sync. Offline, whatever is cached stays in effect.
import { allTests, getReference, putReference, type LocalTest } from "../db/testStore";

/** Reference-store key for the tester's own Testing Company id. */
const OWN_COMPANY_KEY = "testerCompany";
const logoKey = (companyId: string) => `companyLogo:${companyId.toLowerCase()}`;

/** A cached logo. dataUrl null = the company has no printable logo (none uploaded, or a legacy
 * format the report can't embed) — remembered so the report doesn't go looking for it. */
interface CachedLogo {
  dataUrl: string | null;
}

type LogoType = "image/png" | "image/jpeg";

/** PNG / JPEG from the magic bytes — the only formats pdfmake embeds reliably. */
export function sniffImageType(bytes: Uint8Array): LogoType | null {
  const png = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
  if (bytes.length >= png.length && png.every((b, i) => bytes[i] === b)) return "image/png";
  if (bytes.length >= 3 && bytes[0] === 0xff && bytes[1] === 0xd8 && bytes[2] === 0xff) return "image/jpeg";
  return null;
}

export function bytesToDataUrl(bytes: Uint8Array, type: LogoType): string {
  let bin = "";
  // Chunked: String.fromCharCode(...all) overflows the argument limit on a 1 MB logo.
  for (let i = 0; i < bytes.length; i += 0x8000) {
    bin += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
  }
  return `data:${type};base64,${btoa(bin)}`;
}

/** True for a PNG/JPEG data URL whose bytes really are what it says — so a damaged cache entry is
 * skipped rather than handed to pdfmake. */
export function isEmbeddableLogo(dataUrl: unknown): dataUrl is string {
  if (typeof dataUrl !== "string") return false;
  const m = /^data:(image\/(?:png|jpeg));base64,([A-Za-z0-9+/]+={0,2})$/.exec(dataUrl);
  if (!m) return false;
  try {
    // The first 16 base64 characters decode to the first 12 bytes — enough for either signature.
    const head = atob(m[2].slice(0, 16));
    const bytes = Uint8Array.from(head, (c) => c.charCodeAt(0));
    return sniffImageType(bytes) === m[1];
  } catch {
    return false;
  }
}

async function readOwnCompanyId(): Promise<string | null> {
  try {
    const rows = (await getReference(OWN_COMPANY_KEY))?.rows as { id?: unknown } | undefined;
    return typeof rows?.id === "string" && rows.id.length > 0 ? rows.id : null;
  } catch {
    return null;
  }
}

/** Pull the tester's own company from their profile. Only a successful answer replaces the cache —
 * including "no company", so a tester removed from a company stops printing its logo. */
async function refreshOwnCompany(): Promise<void> {
  try {
    const res = await fetch("/api/profile/company", { headers: { Accept: "application/json" }, redirect: "manual" });
    if (!res.ok) return;
    const dto = (await res.json()) as { id?: unknown };
    await putReference({ key: OWN_COMPANY_KEY, rows: { id: typeof dto?.id === "string" ? dto.id : null } });
  } catch {
    // Offline — the cached company stays in effect.
  }
}

/** Conditional re-download of one company's logo into the cache. Never throws. */
export async function refreshCompanyLogo(companyId: string, timeoutMs = 15_000): Promise<void> {
  const key = logoKey(companyId);
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  try {
    const cached = await getReference(key).catch(() => undefined);
    const headers: Record<string, string> = { Accept: "image/png,image/jpeg,*/*" };
    if (cached?.version) headers["If-None-Match"] = cached.version;
    const res = await fetch(`/api/testing-companies/${encodeURIComponent(companyId)}/logo`, {
      headers,
      redirect: "manual",
      // Bypass the HTTP cache: the ETag round-trip above is the freshness check, and the 304 has to
      // reach this code rather than be absorbed by the browser.
      cache: "no-store",
      signal: controller.signal,
    });
    if (res.status === 304) return;
    if (res.status === 404) {
      // Removed (or never uploaded): forget any cached copy so the report stops printing it.
      await putReference({ key, version: null, rows: { dataUrl: null } satisfies CachedLogo });
      return;
    }
    // Signed out, server error, redirect: keep what we have.
    if (!res.ok || res.type === "opaqueredirect") return;

    const bytes = new Uint8Array(await res.arrayBuffer());
    const type = sniffImageType(bytes);
    await putReference({
      key,
      version: res.headers.get("ETag"),
      rows: { dataUrl: type ? bytesToDataUrl(bytes, type) : null } satisfies CachedLogo,
    });
  } catch {
    // Offline or timed out — the cached logo (if any) stays in effect.
  } finally {
    clearTimeout(timer);
  }
}

/** App-load / post-sync refresh: the tester's own company, then the logo of it and of every company
 * a test on this device was done for (a tester who changed company still reprints old tests with
 * the old company's logo). Never throws. */
export async function refreshCompanyLogos(): Promise<void> {
  try {
    await refreshOwnCompany();
    const ids = new Set<string>();
    const own = await readOwnCompanyId();
    if (own) ids.add(own.toLowerCase());
    for (const t of await allTests()) {
      if (t.testingCompanyId) ids.add(t.testingCompanyId.toLowerCase());
    }
    for (const id of ids) await refreshCompanyLogo(id);
  } catch {
    // Best-effort — a missing logo never blocks anything.
  }
}

/** The company whose logo a test's report prints: the company the server stamped on it, or — for
 * a test not synced yet — the tester's own company, which is what the server will stamp. */
async function logoCompanyFor(test: LocalTest): Promise<string | null> {
  if (test.testingCompanyId !== undefined) return test.testingCompanyId;
  return readOwnCompanyId();
}

/** The logo (a PNG/JPEG data URL) to print on this test's report, or null for none. Cache first, so
 * it works offline. Nothing cached yet (a device that hasn't synced since this shipped, or the
 * admin / colleague read-only view) — or `refresh` asked for — tries one quick fetch. Never throws:
 * a missing or broken logo must never stop a report. */
export async function reportLogoFor(test: LocalTest, opts: { refresh?: boolean } = {}): Promise<string | null> {
  try {
    const companyId = await logoCompanyFor(test);
    if (!companyId) return null;
    const key = logoKey(companyId);
    if (opts.refresh || (await getReference(key)) === undefined) await refreshCompanyLogo(companyId, 5_000);
    const dataUrl = ((await getReference(key))?.rows as CachedLogo | undefined)?.dataUrl;
    return isEmbeddableLogo(dataUrl) ? dataUrl : null;
  } catch {
    return null;
  }
}
