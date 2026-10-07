import { describe, it, expect, vi, afterEach } from "vitest";

// The generator chunks are ~2.4 MB behind dynamic imports, so a device that has never printed
// online doesn't have them. What matters is that the failure is actionable rather than mute, and
// that warming can never take the app down with it.
describe("report generator chunks", () => {
  afterEach(() => {
    vi.resetModules();
    vi.doUnmock("pdfmake/build/pdfmake");
    Reflect.deleteProperty(globalThis.navigator as object, "connection");
  });

  it("raises an actionable error when the generator can't be loaded", async () => {
    vi.doMock("pdfmake/build/pdfmake", () => {
      throw new Error("Failed to fetch dynamically imported module");
    });

    const { loadPdfMake, ReportGeneratorUnavailableError } = await import("./generatorChunks");

    const error = await loadPdfMake().catch((e: unknown) => e);
    expect(error).toBeInstanceOf(ReportGeneratorUnavailableError);
    // The tester has to know what to DO — "connect once" is the whole point of the message.
    expect((error as Error).message).toContain("Connect to the internet once");
  });

  // Warming is opportunistic. If it threw, it would take out the sync that triggered it.
  it("reports failure rather than throwing when warming can't complete", async () => {
    vi.doMock("pdfmake/build/pdfmake", () => {
      throw new Error("offline");
    });

    const { warmReportGenerator } = await import("./generatorChunks");

    await expect(warmReportGenerator()).resolves.toBe(false);
  });

  // Asked before work nobody requested at that moment (capturing the Final Report at sign-off): a
  // failed import stays failed for the page, so it mustn't be tried just to find out.
  describe("whether the generator is on the device", () => {
    afterEach(() => {
      Reflect.deleteProperty(globalThis, "caches");
    });

    const fakeCaches = (paths: string[]) =>
      Object.defineProperty(globalThis, "caches", {
        configurable: true,
        value: {
          keys: async () => ["autorep-abc"],
          open: async () => ({ keys: async () => paths.map((p) => new Request(`http://localhost${p}`)) }),
        },
      });

    it("is not, on a page that hasn't loaded it and a device whose worker doesn't hold it", async () => {
      fakeCaches(["/js/dist/autorep.js", "/css/site.css"]);
      const { reportGeneratorOnDevice } = await import("./generatorChunks");
      await expect(reportGeneratorOnDevice()).resolves.toBe(false);
    });

    it("is, when the service worker holds pdfmake and its fonts", async () => {
      fakeCaches(["/js/dist/chunks/pdfmake-ABC123.js", "/js/dist/chunks/vfs_fonts-DEF456.js"]);
      const { reportGeneratorOnDevice } = await import("./generatorChunks");
      await expect(reportGeneratorOnDevice()).resolves.toBe(true);
    });

    it("is, once this page has loaded it — whatever the caches say", async () => {
      fakeCaches([]);
      const { loadPdfMake, reportGeneratorOnDevice } = await import("./generatorChunks");
      await loadPdfMake();
      await expect(reportGeneratorOnDevice()).resolves.toBe(true);
    });
  });

  it("skips the pre-emptive download when the tester has asked to save data", async () => {
    Object.defineProperty(globalThis.navigator, "connection", {
      value: { saveData: true },
      configurable: true,
    });

    const { warmReportGenerator } = await import("./generatorChunks");

    await expect(warmReportGenerator()).resolves.toBe(false);
  });
});
