// pdfmake, its font pack and pdf-lib are loaded as lazy chunks — roughly 2.4 MB minified against
// a 261 KB main bundle, so pulling them into every page load would make the app about eighteen
// times heavier to open.
//
// The cost of that split: installing the PWA does NOT fetch them. A device only holds them once
// it has generated a report while online, because the service worker caches what is actually
// requested. So a brand-new iPad, a reinstall, or a device whose storage was evicted can capture
// a test on-farm and then fail to print it — at the worst possible moment, with the work done.
//
// Two answers here: warm the chunks while a connection demonstrably exists, and when they really
// are missing, say so in terms a tester can act on.

/** Raised when the generator chunks aren't on the device and can't be fetched. */
export class ReportGeneratorUnavailableError extends Error {
  constructor() {
    super(
      "This device hasn't downloaded the report generator yet. Connect to the internet once " +
        "and reports will work offline from then on.",
    );
    this.name = "ReportGeneratorUnavailableError";
  }
}

export interface PdfMakeModules {
  pdfMake: unknown;
  vfs: unknown;
}

/** pdfmake and its fonts have loaded in this page — importing them again needs no network. */
let pdfMakeLoaded = false;

export async function loadPdfMake(): Promise<PdfMakeModules> {
  try {
    const [pdfMakeModule, vfsModule] = await Promise.all([
      import("pdfmake/build/pdfmake"),
      import("pdfmake/build/vfs_fonts"),
    ]);
    pdfMakeLoaded = true;
    return {
      pdfMake: (pdfMakeModule as { default?: unknown }).default ?? pdfMakeModule,
      vfs: (vfsModule as { default?: unknown }).default ?? vfsModule,
    };
  } catch {
    throw new ReportGeneratorUnavailableError();
  }
}

/**
 * Whether a report can be laid out now without touching the network: pdfmake and its fonts are
 * loaded in this page already, or the service worker holds their chunks. For work nobody asked for
 * at that moment (capturing the Final Report at sign-off), ask this first: a dynamic import that
 * fails — no signal, chunks never cached — stays failed for the life of the page, so trying would
 * leave Download report broken here even after the signal came back.
 */
export async function reportGeneratorOnDevice(): Promise<boolean> {
  if (pdfMakeLoaded) return true;
  try {
    if (typeof caches === "undefined") return false;
    const paths: string[] = [];
    for (const name of await caches.keys()) {
      for (const request of await (await caches.open(name)).keys()) paths.push(new URL(request.url).pathname);
    }
    return ["/chunks/pdfmake-", "/chunks/vfs_fonts-"].every((chunk) => paths.some((p) => p.includes(chunk)));
  } catch {
    return false;
  }
}

export async function loadPdfLib(): Promise<typeof import("pdf-lib")> {
  try {
    return await import("pdf-lib");
  } catch {
    throw new ReportGeneratorUnavailableError();
  }
}

/**
 * Pulls the generator chunks down so the service worker caches them, letting a tester print
 * on-farm on a device that has never printed before. Call it when a connection has just proven
 * itself — not on page load, where a weak signal at a farm would mean dragging 2.4 MB over mobile
 * data for no reason.
 *
 * Safe to call repeatedly: within a page the module registry answers instantly, and across page
 * loads the service worker serves them from cache. Returns whether they're now available.
 */
export async function warmReportGenerator(): Promise<boolean> {
  // Respect an explicit "save data" preference — this is a large, purely pre-emptive download.
  // Not supported on iPad Safari, so treat it as best-effort rather than a reliable gate.
  const nav = globalThis.navigator as { connection?: { saveData?: boolean } } | undefined;
  if (nav?.connection?.saveData) return false;

  try {
    await Promise.all([
      import("pdfmake/build/pdfmake"),
      import("pdfmake/build/vfs_fonts"),
      import("pdf-lib"),
    ]);
    pdfMakeLoaded = true;
    return true;
  } catch {
    // Offline, or the fetch failed. Nothing to do — the next successful sync tries again.
    return false;
  }
}
