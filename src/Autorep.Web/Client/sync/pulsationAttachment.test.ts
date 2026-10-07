import "fake-indexeddb/auto";
import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { allTests, deleteTest, getTest, putTest, type LocalTest, type PulsationAttachment } from "../db/testStore";
import { defaultMachineConfiguration } from "../wizard/types";
import {
  RETAIN_DAYS,
  attachmentBase64,
  keepHeldBytes,
  letGoOfHeldAttachments,
  shouldLetGo,
  withoutBytes,
} from "./pulsationAttachment";

const NOW = new Date("2026-10-20T00:00:00.000Z");
const LONG_AGO = "2026-10-01T00:00:00.000Z"; // well past the retention window
const RECENT = "2026-10-18T00:00:00.000Z";

const pdf = (over: Partial<PulsationAttachment> = {}): PulsationAttachment => ({
  name: "pulse.pdf",
  base64: "JVBERi0xLjQ=",
  size: 9,
  attachedAt: LONG_AGO,
  ...over,
});

function test(id: string, over: Partial<LocalTest> = {}): LocalTest {
  return {
    id,
    farmName: "Kowhai Flats",
    config: defaultMachineConfiguration(),
    currentStep: "ReviewSignOff",
    visualFaults: {},
    attestations: [],
    readings: {},
    recommendations: {},
    dataFields: {},
    createdAt: LONG_AGO,
    updatedAt: LONG_AGO,
    markedCompleteAt: LONG_AGO,
    syncState: "uploaded",
    everUploaded: true,
    pulsationPdf: pdf(),
    ...over,
  };
}

const realFetch = globalThis.fetch;
beforeEach(async () => {
  for (const t of await allTests()) await deleteTest(t.id);
});
afterEach(() => {
  globalThis.fetch = realFetch;
});

describe("shouldLetGo", () => {
  it(`lets go of a complete, synced test's PDF held for more than ${RETAIN_DAYS} days`, () => {
    expect(shouldLetGo(test("a"), NOW)).toBe(true);
  });

  it("keeps it while it's recent — the tester may still print it at the farm gate", () => {
    expect(shouldLetGo(test("a", { pulsationPdf: pdf({ attachedAt: RECENT }) }), NOW)).toBe(false);
    // Fetched back for a reprint restarts the clock.
    expect(shouldLetGo(test("a", { pulsationPdf: pdf({ heldSince: RECENT }) }), NOW)).toBe(false);
  });

  it("never lets go before the server is known to hold this exact version", () => {
    expect(shouldLetGo(test("a", { syncState: "local-only" }), NOW)).toBe(false);
    expect(shouldLetGo(test("a", { everUploaded: false }), NOW)).toBe(false);
    expect(shouldLetGo(test("a", { markedCompleteAt: null }), NOW)).toBe(false);
  });

  it("has nothing to let go of without bytes", () => {
    expect(shouldLetGo(test("a", { pulsationPdf: withoutBytes(pdf()) }), NOW)).toBe(false);
    expect(shouldLetGo(test("a", { pulsationPdf: null }), NOW)).toBe(false);
  });
});

describe("letGoOfHeldAttachments", () => {
  it("turns the attachment into a pointer, keeping what the report and the audit trail show", async () => {
    await putTest(test("old"));
    await putTest(test("recent", { pulsationPdf: pdf({ attachedAt: RECENT }) }));

    expect(await letGoOfHeldAttachments(await allTests(), NOW)).toBe(1);

    const old = (await getTest("old"))!.pulsationPdf!;
    expect(old.base64).toBeUndefined();
    expect(old).toMatchObject({ name: "pulse.pdf", size: 9, attachedAt: LONG_AGO, onServer: true });
    expect((await getTest("recent"))!.pulsationPdf!.base64).toBeDefined();
    expect((await getTest("old"))!.syncState).toBe("uploaded"); // nothing new to send
  });

  it("re-reads before writing, so an edit that landed meanwhile is never overwritten", async () => {
    const stale = test("edited");
    await putTest({ ...stale, syncState: "local-only", notes: "edited after the list was read" });

    expect(await letGoOfHeldAttachments([stale], NOW)).toBe(0);

    const now = (await getTest("edited"))!;
    expect(now.notes).toBe("edited after the list was read");
    expect(now.pulsationPdf!.base64).toBeDefined();
  });
});

describe("keepHeldBytes — a pull replacing a clean copy", () => {
  it("keeps the bytes this device holds for the same attachment", () => {
    const existing = test("a", { pulsationPdf: pdf({ heldSince: RECENT }) });
    const incoming = test("a", { pulsationPdf: withoutBytes(pdf()), notes: "from the server" });

    const merged = keepHeldBytes(existing, incoming);

    expect(merged.notes).toBe("from the server");
    expect(merged.pulsationPdf).toEqual(existing.pulsationPdf);
  });

  it("takes the server's attachment when it is a different one", () => {
    const existing = test("a", { pulsationPdf: pdf({ name: "old.pdf" }) });
    const incoming = test("a", { pulsationPdf: withoutBytes(pdf({ name: "new.pdf" })) });

    expect(keepHeldBytes(existing, incoming).pulsationPdf?.name).toBe("new.pdf");
    expect(keepHeldBytes(existing, incoming).pulsationPdf?.base64).toBeUndefined();
  });
});

describe("attachmentBase64 — what a report gets", () => {
  it("uses the bytes on the device when it has them, without asking the server", async () => {
    globalThis.fetch = vi.fn() as unknown as typeof fetch;
    expect(await attachmentBase64(test("a"))).toBe("JVBERi0xLjQ=");
    expect(globalThis.fetch).not.toHaveBeenCalled();
  });

  it("fetches them back from the test that holds them, and holds them again", async () => {
    const pointer = test("v2", { pulsationPdf: { ...withoutBytes(pdf()), serverTestId: "v1" } });
    await putTest(pointer);
    const urls: string[] = [];
    globalThis.fetch = vi.fn(async (url: unknown) => {
      urls.push(String(url));
      return new Response(new Uint8Array([0x25, 0x50, 0x44, 0x46]), { status: 200 });
    }) as unknown as typeof fetch;

    expect(await attachmentBase64(pointer)).toBe("JVBERg==");

    expect(urls).toEqual(["/api/sync/tests/v1/pulsation-pdf"]);
    const held = (await getTest("v2"))!.pulsationPdf!;
    expect(held.base64).toBe("JVBERg==");
    expect(held.onServer).toBeUndefined();
    expect(held.heldSince).toBeDefined();
  });

  it("is null offline, so the report can say why the PDF isn't in it", async () => {
    globalThis.fetch = vi.fn(async () => {
      throw new TypeError("Failed to fetch");
    }) as unknown as typeof fetch;

    expect(await attachmentBase64(test("a", { pulsationPdf: withoutBytes(pdf()) }))).toBeNull();
  });
});
