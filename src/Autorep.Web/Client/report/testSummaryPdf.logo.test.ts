// Renders the real Test Summary through pdfmake (it runs under Node) to prove the offline logo path
// end to end: the logo synced into IndexedDB is embedded in the PDF, and a logo pdfmake can't
// decode costs the report its logo — never the report.
import "fake-indexeddb/auto";
import { describe, it, expect, afterEach, beforeEach, vi } from "vitest";
import { downloadTestSummaryPdf } from "./testSummaryPdf";
import { refreshCompanyLogo } from "../sync/companyLogoSync";
import { defaultMachineConfiguration } from "../wizard/types";
import type { LocalTest } from "../db/testStore";

const PNG = Uint8Array.from(
  atob("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg=="),
  (c) => c.charCodeAt(0),
);
// Right signature, broken body: passes the cheap byte check, fails when pdfmake decodes it.
const CORRUPT_PNG = PNG.slice(0, 20);

function completedTest(companyId: string): LocalTest {
  const now = "2026-09-23T00:00:00.000Z";
  return {
    id: crypto.randomUUID(),
    farmName: "Sunny Acres",
    farm: { name: "Sunny Acres" },
    config: defaultMachineConfiguration(),
    currentStep: "ReviewSignOff",
    visualFaults: {},
    attestations: [],
    readings: {},
    recommendations: {},
    dataFields: {},
    createdAt: now,
    updatedAt: now,
    markedCompleteAt: now,
    syncState: "uploaded",
    testingCompanyId: companyId,
  };
}

const realFetch = globalThis.fetch;
let downloaded: Blob[] = [];

beforeEach(() => {
  downloaded = [];
  // The browser download: capture the blob instead.
  vi.spyOn(URL, "createObjectURL").mockImplementation((b) => {
    downloaded.push(b as Blob);
    return "blob:test";
  });
  vi.spyOn(URL, "revokeObjectURL").mockImplementation(() => {});
  (globalThis as { document?: unknown }).document = { createElement: () => ({ click() {} }) };
});

afterEach(() => {
  globalThis.fetch = realFetch;
  vi.restoreAllMocks();
  delete (globalThis as { document?: unknown }).document;
});

async function syncLogo(companyId: string, bytes: Uint8Array) {
  globalThis.fetch = (async () =>
    new Response(new Blob([bytes as BlobPart]), { status: 200, headers: { ETag: `"${companyId}"` } })) as typeof fetch;
  await refreshCompanyLogo(companyId);
  // Then go offline — the report must print from what's on the device.
  globalThis.fetch = (() => Promise.reject(new TypeError("offline"))) as typeof fetch;
}

async function pdfText(): Promise<string> {
  expect(downloaded).toHaveLength(1);
  return Buffer.from(await downloaded[0].arrayBuffer()).toString("latin1");
}

describe("downloadTestSummaryPdf — company logo", () => {
  it("embeds the synced logo while offline", async () => {
    const company = crypto.randomUUID();
    await syncLogo(company, PNG);

    await downloadTestSummaryPdf(completedTest(company));

    const pdf = await pdfText();
    expect(pdf.startsWith("%PDF")).toBe(true);
    expect(pdf).toContain("/Subtype /Image");
  });

  it("still produces the report, without the logo, when the logo is corrupt", async () => {
    const company = crypto.randomUUID();
    await syncLogo(company, CORRUPT_PNG);

    await downloadTestSummaryPdf(completedTest(company));

    const pdf = await pdfText();
    expect(pdf.startsWith("%PDF")).toBe(true);
    expect(pdf).not.toContain("/Subtype /Image");
  });
});
