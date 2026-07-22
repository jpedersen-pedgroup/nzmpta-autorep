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

  it("skips the pre-emptive download when the tester has asked to save data", async () => {
    Object.defineProperty(globalThis.navigator, "connection", {
      value: { saveData: true },
      configurable: true,
    });

    const { warmReportGenerator } = await import("./generatorChunks");

    await expect(warmReportGenerator()).resolves.toBe(false);
  });
});
