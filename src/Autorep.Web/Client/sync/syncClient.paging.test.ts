// The first pull on a device is the tester's whole history: it comes in pages, newest first, is
// stored page by page, resumes where it stopped, and leaves the analyser PDFs on the server.
import "fake-indexeddb/auto";
import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { allTests, deleteTest, getReference, getTest, putReference, putTest, type LocalTest } from "../db/testStore";
import { PULL_PAGE_SIZE, syncAll } from "./syncClient";
import { defaultMachineConfiguration } from "../wizard/types";

const W1 = "2026-10-07T01:00:00.000Z";
const W2 = "2026-10-07T01:05:00.000Z";

function summary(id: string, payload?: Partial<LocalTest>) {
  const now = "2026-10-01T00:00:00.000Z";
  return {
    clientId: id,
    farmName: `Farm ${id}`,
    createdAt: now,
    markedCompleteAt: now,
    config: null,
    payloadJson: payload
      ? JSON.stringify({
          id,
          farmName: `Farm ${id}`,
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
          ...payload,
        })
      : null,
  };
}

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } });

const realFetch = globalThis.fetch;
let pulls: URL[] = [];

/** Pull pages by cursor; every other endpoint syncAll touches answers 404 (nothing to do). */
function stubPages(pages: Record<string, () => Response>) {
  pulls = [];
  globalThis.fetch = vi.fn(async (input: unknown) => {
    const url = new URL(String(input), "https://autorep.test");
    if (url.pathname !== "/api/sync/tests") return new Response(null, { status: 404 });
    pulls.push(url);
    const page = pages[url.searchParams.get("cursor") ?? "first"];
    if (!page) throw new Error(`unexpected cursor ${url.searchParams.get("cursor")}`);
    return page();
  }) as unknown as typeof fetch;
}

beforeEach(async () => {
  for (const t of await allTests()) await deleteTest(t.id);
  await putReference({ key: "testPullWatermark", version: null });
  await putReference({ key: "testPullProgress", rows: null });
});

afterEach(() => {
  globalThis.fetch = realFetch;
});

describe("the paged pull", () => {
  it("fetches every page, asks for the PDFs to stay on the server, and keeps the FIRST page's watermark", async () => {
    stubPages({
      first: () => json({ watermark: W1, tests: [summary("a"), summary("b")], next: "2" }),
      "2": () => json({ watermark: W2, tests: [summary("c")], next: null }),
    });

    const result = await syncAll();

    expect(result.pulled).toBe(3);
    expect((await allTests()).map((t) => t.id).sort()).toEqual(["a", "b", "c"]);
    expect(pulls[0].searchParams.get("limit")).toBe(String(PULL_PAGE_SIZE));
    expect(pulls[0].searchParams.get("attachments")).toBe("omit");
    expect(pulls[1].searchParams.get("cursor")).toBe("2");
    // Anything written while the pages came in arrives next time, because the watermark predates it.
    expect((await getReference("testPullWatermark"))?.version).toBe(W1);
    expect((await getReference("testPullProgress"))?.rows).toBeNull();
  });

  it("keeps the pages it got when the connection drops, and resumes from there", async () => {
    stubPages({
      first: () => json({ watermark: W1, tests: [summary("a")], next: "1" }),
      "1": () => {
        throw new TypeError("Failed to fetch");
      },
    });

    await expect(syncAll()).rejects.toThrow();
    expect(await getTest("a")).toBeDefined();
    expect((await getReference("testPullWatermark"))?.version).toBeNull();

    stubPages({
      "1": () => json({ watermark: W2, tests: [summary("b")], next: null }),
    });
    await syncAll();

    expect(pulls.map((u) => u.searchParams.get("cursor"))).toEqual(["1"]);
    expect(await getTest("b")).toBeDefined();
    expect((await getReference("testPullWatermark"))?.version).toBe(W1);
  });

  it("starts again from the top when the server no longer recognises a saved cursor", async () => {
    await putReference({ key: "testPullProgress", rows: { since: null, watermark: W1, cursor: "999" } });
    stubPages({
      "999": () => json({ error: "Unrecognised cursor" }, 400),
      first: () => json({ watermark: W2, tests: [summary("a")], next: null }),
    });

    await syncAll();

    expect(pulls.map((u) => u.searchParams.get("cursor"))).toEqual(["999", null]);
    expect((await getReference("testPullWatermark"))?.version).toBe(W2);
  });

  it("doesn't throw away analyser PDF bytes the device still holds when the server's copy replaces its own", async () => {
    const held = { name: "pulse.pdf", base64: "JVBERi0=", size: 6, attachedAt: "2026-10-06T00:00:00.000Z" };
    await putTest({
      ...JSON.parse(summary("a", {}).payloadJson!),
      syncState: "uploaded",
      everUploaded: true,
      pulsationPdf: held,
    } as LocalTest);
    stubPages({
      first: () =>
        json({
          watermark: W1,
          tests: [summary("a", { notes: "edited on another device", pulsationPdf: { ...held, base64: undefined, onServer: true } })],
          next: null,
        }),
    });

    await syncAll();

    const a = (await getTest("a"))!;
    expect(a.notes).toBe("edited on another device");
    expect(a.pulsationPdf?.base64).toBe("JVBERi0=");
  });
});
