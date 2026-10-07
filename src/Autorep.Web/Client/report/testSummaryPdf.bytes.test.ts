// The report as bytes: what downloadTestSummaryPdf saves, and what the device sends the server as
// the Final Report as signed off. The stored copy is made again at send time rather than kept from
// sign-off, which only works if the same test gives the same bytes — so that is pinned here.
import "fake-indexeddb/auto";
import { describe, it, expect, afterEach, vi } from "vitest";
import { PDFDocument } from "pdf-lib";
import { finalReportPdf, reportPdfBytes } from "./testSummaryPdf";
import { defaultMachineConfiguration } from "../wizard/types";
import type { LocalTest } from "../db/testStore";

const SIGNED_OFF = "2026-10-07T01:15:00.000Z";

function signedOffTest(over: Partial<LocalTest> = {}): LocalTest {
  return {
    id: "final-1",
    farmName: "Kowhai Flats",
    farm: { name: "Kowhai Flats", supplyNumber: "40123", milkCompanyName: "Fonterra" },
    config: { ...defaultMachineConfiguration(), clusterCount: 20, pulsatorCount: 10 },
    currentStep: "ReviewSignOff",
    visualFaults: { "vp.wick": { status: "fault", severity: "Minor", observation: "Oil Wicks Dirty" } },
    attestations: [{ step: "ReviewSignOff", attestedAt: SIGNED_OFF, text: "I confirm." }],
    readings: { "tr.workingVacuum": 48 },
    recommendations: {},
    dataFields: {},
    testedBy: { name: "Ellie Offlinetester" },
    nextTestDate: "2027-10-07",
    createdAt: "2026-10-06T22:00:00.000Z",
    updatedAt: SIGNED_OFF,
    markedCompleteAt: SIGNED_OFF,
    syncState: "uploaded",
    everUploaded: true,
    ...over,
  };
}

/** An analyser export of `pages` pages, base64 — what the sign-off step stores. */
async function analyserPdf(pages: number): Promise<string> {
  const doc = await PDFDocument.create();
  for (let i = 0; i < pages; i++) doc.addPage([400, 300]).drawText(`Analyser page ${i + 1}`, { x: 40, y: 150 });
  return Buffer.from(await doc.save()).toString("base64");
}

const pageCount = async (bytes: Uint8Array) => (await PDFDocument.load(bytes)).getPageCount();
const same = (a: Uint8Array, b: Uint8Array) => Buffer.from(a).equals(Buffer.from(b));

/** Runs `fn` with the clock (Date only — pdfmake's own timers keep running) at `iso`. */
async function at<T>(iso: string, fn: () => Promise<T>): Promise<T> {
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date(iso));
  try {
    return await fn();
  } finally {
    vi.useRealTimers();
  }
}

const realFetch = globalThis.fetch;
afterEach(() => {
  globalThis.fetch = realFetch;
  vi.useRealTimers();
});

describe("the report as bytes", () => {
  it("is a PDF, named as the download is", async () => {
    const pdf = await reportPdfBytes(signedOffTest());
    expect(Buffer.from(pdf.bytes.subarray(0, 5)).toString("ascii")).toBe("%PDF-");
    expect(pdf.fileName).toBe("Test Summary - Kowhai Flats - 2026-10-07.pdf");
    expect(pdf.analyser).toBe("none");
  }, 30_000);

  it("as signed off, is the same bytes whenever it is made", async () => {
    const first = await at("2026-10-07T01:15:04.000Z", () => finalReportPdf(signedOffTest()));
    const days = await at("2026-10-09T18:40:00.000Z", () => finalReportPdf(signedOffTest()));
    expect(same(first.bytes, days.bytes)).toBe(true);
  }, 30_000);

  it("unpinned — a download — carries when it was made, so it differs", async () => {
    const one = await at("2026-10-07T01:15:04.000Z", () => reportPdfBytes(signedOffTest()));
    const two = await at("2026-10-09T18:40:00.000Z", () => reportPdfBytes(signedOffTest()));
    expect(same(one.bytes, two.bytes)).toBe(false);
  }, 30_000);

  it("as signed off, appends the analyser's pages and still comes out the same", async () => {
    const base64 = await analyserPdf(3);
    const test = signedOffTest({ pulsationPdf: { name: "pulse.pdf", base64, size: 1000, attachedAt: SIGNED_OFF } });
    const alone = await reportPdfBytes(signedOffTest(), { generatedAt: SIGNED_OFF });

    const first = await at("2026-10-07T01:16:00.000Z", () => finalReportPdf(test));
    const later = await at("2026-10-08T09:00:00.000Z", () => finalReportPdf(test));

    expect(first.analyser).toBe("appended");
    expect(await pageCount(first.bytes)).toBe((await pageCount(alone.bytes)) + 3);
    expect(same(first.bytes, later.bytes)).toBe(true);
  }, 30_000);

  it("says when the analyser's bytes are on the server and it can't be reached", async () => {
    globalThis.fetch = vi.fn(async () => {
      throw new TypeError("Failed to fetch");
    }) as unknown as typeof fetch;
    const test = signedOffTest({ pulsationPdf: { name: "pulse.pdf", size: 1000, attachedAt: SIGNED_OFF, onServer: true } });

    const pdf = await finalReportPdf(test);

    expect(pdf.analyser).toBe("unreachable");
    expect(Buffer.from(pdf.bytes.subarray(0, 5)).toString("ascii")).toBe("%PDF-"); // the rest of it, still
  }, 30_000);

  it("says when the attachment can't be read, and gives the summary alone", async () => {
    const test = signedOffTest({
      pulsationPdf: { name: "broken.pdf", base64: Buffer.from("not a pdf at all").toString("base64"), size: 16, attachedAt: SIGNED_OFF },
    });

    const pdf = await finalReportPdf(test);
    const alone = await reportPdfBytes({ ...test, pulsationPdf: null }, { generatedAt: SIGNED_OFF });

    expect(pdf.analyser).toBe("unreadable");
    expect(await pageCount(pdf.bytes)).toBe(await pageCount(alone.bytes));
  }, 30_000);

  it("leaves the analyser out when only other sections are asked for", async () => {
    const test = signedOffTest({ pulsationPdf: { name: "pulse.pdf", base64: await analyserPdf(2), size: 1000, attachedAt: SIGNED_OFF } });
    const pdf = await reportPdfBytes(test, { only: ["summary"] });
    expect(pdf.analyser).toBe("none");
    expect(pdf.fileName).toContain("selected sections");
  }, 30_000);
});
