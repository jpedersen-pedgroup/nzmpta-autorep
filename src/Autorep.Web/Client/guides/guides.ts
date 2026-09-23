// The Help & guides work-instruction PDFs, as the client app sees them. The metadata is imported
// from Guides/guides.json — the same file the server lists and authorises from — so there is one
// place to edit when a guide is replaced (see the notes at the top of that file).
//
// The server-rendered Help page can't open offline (page HTML is never cached — see sw.js), so the
// tester app carries its own links straight to the PDF, and keeps the PDF warm after each sync so
// the service worker holds a copy for the shed with no coverage.
import catalog from "../../Guides/guides.json";

export interface GuideInfo {
  id: string;
  title: string;
  description: string;
  version: string;
  /** ISO yyyy-MM-dd. */
  date: string;
  /** File name under /guides/ — lowercase, digits and hyphens, .pdf. */
  file: string;
  /** Role keys (Domain/Roles.cs) that may see and download it. */
  roles: string[];
}

export const GUIDES: readonly GuideInfo[] = catalog.guides;

/** The Tester role key, as in Domain/Roles.cs — the only role the offline tester app serves. */
export const TESTER_ROLE = "Tester";

/**
 * Unversioned on purpose: the service worker keys its offline copy on the path, and revalidates
 * with the ETag while online, so this always opens the newest version the device can reach.
 */
export function guideHref(guide: GuideInfo): string {
  return `/guides/${guide.file}`;
}

/** The guides an account holding any of `roles` may see (mirrors GuideCatalog.VisibleTo). */
export function guidesForRoles(roles: readonly string[]): GuideInfo[] {
  return GUIDES.filter((g) => g.roles.some((r) => roles.includes(r)));
}

/** The guide a tester reaches from My tests and the wizard header. */
export function testerGuide(): GuideInfo | undefined {
  return GUIDES.find((g) => g.id === "tester");
}

/**
 * Fetches the guides so the service worker keeps a copy (its GUIDE_CACHE fills as the PDF passes
 * through). Called after a successful sync — a connection has just proven itself — never on page
 * load. Once a guide is held, the worker revalidates with the ETag, so a repeat costs a 304.
 *
 * Fire-and-forget safe: never throws. Returns whether every guide is now on the device.
 */
export async function warmGuides(guides: readonly GuideInfo[]): Promise<boolean> {
  const nav = globalThis.navigator as
    | { connection?: { saveData?: boolean }; serviceWorker?: { controller: unknown } }
    | undefined;
  // A pre-emptive download of a few MB: respect "save data" (best-effort — iPad Safari lacks it).
  if (nav?.connection?.saveData) return false;
  // Without a controlling worker nothing would keep the file, so the download would be wasted.
  if (!nav?.serviceWorker?.controller) return false;

  try {
    const responses = await Promise.all(
      guides.map((g) => fetch(guideHref(g), { credentials: "same-origin" })),
    );
    // The worker has already stored each copy before answering; the body isn't needed here.
    for (const r of responses) void r.body?.cancel().catch(() => undefined);
    // A signed-out fetch follows the redirect to the login page — a 200, but not a PDF.
    return responses.every((r) => r.ok && (r.headers.get("Content-Type") ?? "").startsWith("application/pdf"));
  } catch {
    // Offline, or the fetch failed. Nothing to do — the next successful sync tries again.
    return false;
  }
}
