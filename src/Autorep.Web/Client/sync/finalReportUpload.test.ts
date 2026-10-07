// The Final Report as signed off follows its test to the server: queued at sign-off, sent once the
// test is up, retried with a backoff when it can't go, and never sent under the wrong tester.
import "fake-indexeddb/auto";
import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { allTests, deleteReference, deleteTest, putTest, type LocalTest } from "../db/testStore";
import { defaultMachineConfiguration } from "../wizard/types";
import { ReportGeneratorUnavailableError } from "../report/generatorChunks";
import type { AnalyserOutcome, ReportPdf } from "../report/testSummaryPdf";
import { syncAll } from "./syncClient";
import {
  MAX_ATTEMPTS,
  MAX_FINAL_REPORT_BYTES,
  RETRY_MS,
  dueFinalReports,
  flushFinalReports,
  pendingFinalReports,
  queueFinalReport,
  type FlushDeps,
} from "./finalReportUpload";

const NOW = new Date("2026-10-07T02:00:00.000Z");
const PDF = new Uint8Array([0x25, 0x50, 0x44, 0x46, 0x2d, 0x31]); // "%PDF-1"

function signedOff(id: string, over: Partial<LocalTest> = {}): LocalTest {
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
    createdAt: "2026-10-07T00:00:00.000Z",
    updatedAt: "2026-10-07T01:00:00.000Z",
    markedCompleteAt: "2026-10-07T01:00:00.000Z",
    syncState: "uploaded",
    everUploaded: true,
    ...over,
  };
}

function deps(over: Partial<FlushDeps> = {}, analyser: AnalyserOutcome = "none") {
  const put = vi.fn(async (_id: string, _bytes: Uint8Array) => new Response(null, { status: 201 }));
  const generate = vi.fn(async (_t: LocalTest): Promise<ReportPdf> => ({ bytes: PDF, fileName: "r.pdf", analyser }));
  const d: FlushDeps = { put, generate, now: () => NOW, ...over };
  return d as FlushDeps & { put: typeof put; generate: typeof generate };
}

const pending = async () => Object.fromEntries((await pendingFinalReports()).map((p) => [p.testId, p]));

beforeEach(async () => {
  for (const t of await allTests()) await deleteTest(t.id);
  for (const p of await pendingFinalReports()) await deleteReference(`finalReport:${p.testId}`);
});

describe("the Final Report upload queue", () => {
  it("sends a queued report once its test is on the server, then forgets it", async () => {
    await putTest(signedOff("a"));
    await queueFinalReport("a", NOW);
    const d = deps();

    const result = await flushFinalReports(d);

    expect(result).toEqual({ sent: 1, waiting: 0, dropped: 0 });
    expect(d.put).toHaveBeenCalledWith("a", PDF);
    expect(await pendingFinalReports()).toEqual([]);
  });

  it("waits, without counting it as a failure, while the test itself hasn't gone up", async () => {
    await putTest(signedOff("b", { syncState: "local-only", everUploaded: false }));
    await queueFinalReport("b", NOW);
    const d = deps();

    const result = await flushFinalReports(d);

    expect(result).toEqual({ sent: 0, waiting: 1, dropped: 0 });
    expect(d.generate).not.toHaveBeenCalled();
    expect((await pending()).b.attempts).toBe(0);
  });

  it("backs off after the server fails it, and tries again once that time has passed", async () => {
    await putTest(signedOff("c"));
    await queueFinalReport("c", NOW);
    const failing = deps({ put: vi.fn(async () => new Response(null, { status: 503 })) });

    await flushFinalReports(failing);
    const after = (await pending()).c;
    expect(after.attempts).toBe(1);
    expect(after.notBefore).toBe(new Date(NOW.getTime() + RETRY_MS[0]).toISOString());
    expect(after.lastProblem).toContain("503");
    expect(await dueFinalReports(NOW)).toBe(0);

    const tooSoon = deps({ now: () => new Date(NOW.getTime() + RETRY_MS[0] - 1) });
    await flushFinalReports(tooSoon);
    expect(tooSoon.put).not.toHaveBeenCalled();

    const later = deps({ now: () => new Date(NOW.getTime() + RETRY_MS[0]) });
    expect((await flushFinalReports(later)).sent).toBe(1);
    expect(await pendingFinalReports()).toEqual([]);
  });

  it("stops at the first sign of no connection, backing that one off and leaving the rest", async () => {
    await putTest(signedOff("d1"));
    await putTest(signedOff("d2"));
    await queueFinalReport("d1", NOW);
    await queueFinalReport("d2", NOW);
    const offline = deps({
      put: vi.fn(async () => {
        throw new TypeError("Failed to fetch");
      }),
    });

    const result = await flushFinalReports(offline);

    expect(offline.put).toHaveBeenCalledTimes(1);
    expect(result).toEqual({ sent: 0, waiting: 2, dropped: 0 });
    const queue = await pending();
    expect(Object.values(queue).map((p) => p.attempts).sort()).toEqual([0, 1]);
  });

  it("waits for sign-in when the session has lapsed, without holding it against the report", async () => {
    await putTest(signedOff("e"));
    await queueFinalReport("e", NOW);

    const result = await flushFinalReports(deps({ put: vi.fn(async () => new Response(null, { status: 401 })) }));

    expect(result.waiting).toBe(1);
    expect((await pending()).e).toMatchObject({ attempts: 0 });
  });

  it("gives up on a report the server refuses for good", async () => {
    await putTest(signedOff("f"));
    await queueFinalReport("f", NOW);
    const warn = vi.spyOn(console, "warn").mockImplementation(() => {});

    const result = await flushFinalReports(deps({ put: vi.fn(async () => new Response(null, { status: 413 })) }));

    expect(result).toEqual({ sent: 0, waiting: 0, dropped: 1 });
    expect(await pendingFinalReports()).toEqual([]);
    warn.mockRestore();
  });

  it("never sends one over the server's limit", async () => {
    await putTest(signedOff("g"));
    await queueFinalReport("g", NOW);
    const warn = vi.spyOn(console, "warn").mockImplementation(() => {});
    const huge = deps({ generate: vi.fn(async () => ({ bytes: new Uint8Array(MAX_FINAL_REPORT_BYTES + 1), fileName: "r.pdf", analyser: "none" as const })) });

    const result = await flushFinalReports(huge);

    expect(huge.put).not.toHaveBeenCalled();
    expect(result.dropped).toBe(1);
    warn.mockRestore();
  });

  it("keeps it queued when the report generator isn't on the device yet", async () => {
    await putTest(signedOff("h"));
    await queueFinalReport("h", NOW);

    await flushFinalReports(deps({ generate: vi.fn(async () => Promise.reject(new ReportGeneratorUnavailableError())) }));

    const left = (await pending()).h;
    expect(left.attempts).toBe(1);
    expect(left.lastProblem).toContain("generator");
  });

  it("won't send a copy missing the analyser pages the tester's had — it waits for them", async () => {
    await putTest(signedOff("i"));
    await queueFinalReport("i", NOW);
    const d = deps({}, "unreachable");

    await flushFinalReports(d);

    expect(d.put).not.toHaveBeenCalled();
    expect((await pending()).i.attempts).toBe(1);
  });

  it("sends the summary alone when the attachment is one pdf-lib can never read — the tester's was the same", async () => {
    await putTest(signedOff("j"));
    await queueFinalReport("j", NOW);
    const d = deps({}, "unreadable");

    expect((await flushFinalReports(d)).sent).toBe(1);
  });

  it("drops a report whose test is no longer a signed-off test on this device", async () => {
    await queueFinalReport("gone", NOW);
    const warn = vi.spyOn(console, "warn").mockImplementation(() => {});

    expect((await flushFinalReports(deps())).dropped).toBe(1);
    warn.mockRestore();
  });

  it("gives up after so many failed attempts", async () => {
    await putTest(signedOff("k"));
    await queueFinalReport("k", NOW);
    const warn = vi.spyOn(console, "warn").mockImplementation(() => {});
    let now = NOW.getTime();
    const failing = deps({ put: vi.fn(async () => new Response(null, { status: 500 })), now: () => new Date(now) });

    for (let i = 0; i < MAX_ATTEMPTS; i++) {
      await flushFinalReports(failing);
      now += 7 * 3_600_000; // past the longest wait
    }

    expect(failing.put).toHaveBeenCalledTimes(MAX_ATTEMPTS);
    expect(await pendingFinalReports()).toEqual([]);
    warn.mockRestore();
  });

  it("sends nothing from a store that belongs to someone other than the tester signed in", async () => {
    // This page opened the store as the tester the device last knew; the server says another.
    await putTest(signedOff("l")); // opens the store (owner: whoever was current then — nobody)
    await queueFinalReport("l", NOW);
    (globalThis as { __autorepTesterId?: string }).__autorepTesterId = "someone-else";
    try {
      const d = deps();
      expect(await flushFinalReports(d)).toEqual({ sent: 0, waiting: 0, dropped: 0 });
      expect(d.put).not.toHaveBeenCalled();
    } finally {
      delete (globalThis as { __autorepTesterId?: string }).__autorepTesterId;
    }
  });

  it("runs one flush at a time, and a call made during one gets one more run after it", async () => {
    await putTest(signedOff("m1"));
    await queueFinalReport("m1", NOW);
    let release!: () => void;
    const held = new Promise<void>((r) => (release = r));
    const d = deps({
      put: vi.fn(async () => {
        await held;
        return new Response(null, { status: 201 });
      }),
    });

    const first = flushFinalReports(d);
    await new Promise((r) => setTimeout(r, 20));
    // Signed off while the first was sending.
    await putTest(signedOff("m2"));
    await queueFinalReport("m2", NOW);
    const second = flushFinalReports(d);
    const third = flushFinalReports(d);
    release();

    expect((await first).sent).toBe(1);
    expect(await second).toEqual(await third);
    expect(d.put.mock.calls.map((c) => c[0]).sort()).toEqual(["m1", "m2"]);
  });
});

describe("a sync sends the reports queued behind its tests", () => {
  const json = (body: unknown, status = 200) =>
    new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } });
  const realFetch = globalThis.fetch;
  afterEach(() => {
    globalThis.fetch = realFetch;
  });

  it("pushes the signed-off test, then PUTs its report as application/pdf", async () => {
    await putTest(signedOff("n", { syncState: "local-only", everUploaded: false, testedBy: { name: "Ellie" } }));
    await queueFinalReport("n", NOW);
    const puts: { url: string; type: string | null; bytes: Uint8Array }[] = [];
    let reportArrived!: () => void;
    const arrived = new Promise<void>((r) => (reportArrived = r));
    globalThis.fetch = vi.fn(async (url: unknown, init?: RequestInit) => {
      if (init?.method === "POST") return json({ id: "s-n", status: "created" }, 201);
      if (init?.method === "PUT") {
        const body = init.body as Blob;
        puts.push({ url: String(url), type: new Headers(init.headers).get("content-type"), bytes: new Uint8Array(await body.arrayBuffer()) });
        reportArrived();
        return json({ status: "created" }, 201);
      }
      return json({ watermark: "2026-10-07T00:00:00.000Z", tests: [], next: null });
    }) as unknown as typeof fetch;

    await syncAll();
    await Promise.race([arrived, new Promise((_, no) => setTimeout(() => no(new Error("no report was sent")), 20_000))]);

    expect(puts).toHaveLength(1);
    expect(puts[0].url).toBe("/api/sync/final-report/n");
    expect(puts[0].type).toBe("application/pdf");
    expect(Buffer.from(puts[0].bytes.subarray(0, 5)).toString("ascii")).toBe("%PDF-");
    await expect.poll(async () => (await pendingFinalReports()).length).toBe(0);
  }, 30_000);
});
