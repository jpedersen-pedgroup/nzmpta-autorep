// The Final Report as signed off follows its test to the server: captured and queued at sign-off,
// sent once the test is up, retried with a backoff when it can't go, and never sent under the
// wrong tester.
import "fake-indexeddb/auto";
import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { allTests, deleteReference, deleteTest, getTest, putTest, type LocalTest } from "../db/testStore";
import { defaultMachineConfiguration } from "../wizard/types";
import { ReportGeneratorUnavailableError, loadPdfMake } from "../report/generatorChunks";
import type { AnalyserOutcome, ReportPdf } from "../report/testSummaryPdf";
import { syncAll } from "./syncClient";
import {
  MAX_ATTEMPTS,
  MAX_FINAL_REPORT_BYTES,
  RETRY_MS,
  capturesSettled,
  dueFinalReports,
  flushFinalReports,
  keepRetryingFinalReports,
  pendingFinalReports,
  queueFinalReport,
  type FlushDeps,
} from "./finalReportUpload";

const NOW = new Date("2026-10-07T02:00:00.000Z");
const PDF = new Uint8Array([0x25, 0x50, 0x44, 0x46, 0x2d, 0x31]); // "%PDF-1"
const CAPTURED = new Uint8Array([0x25, 0x50, 0x44, 0x46, 0x2d, 0x37]); // "%PDF-7"

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

/** Deps whose generate/complete answer `analyser` and whose PUT answers 201. */
function deps(over: Partial<FlushDeps> = {}, analyser: AnalyserOutcome = "none") {
  const put = vi.fn(async (_id: string, _bytes: Uint8Array) => new Response(null, { status: 201 }));
  const generate = vi.fn(async (_t: LocalTest): Promise<ReportPdf> => ({ bytes: PDF, fileName: "r.pdf", analyser }));
  const complete = vi.fn(async (_t: LocalTest, captured: Uint8Array): Promise<ReportPdf> => ({ bytes: captured, fileName: "r.pdf", analyser }));
  const d: FlushDeps = { put, generate, complete, now: () => NOW, ...over };
  return d as FlushDeps & { put: typeof put; generate: typeof generate; complete: typeof complete };
}

const noCapture = async (): Promise<Uint8Array> => {
  throw new ReportGeneratorUnavailableError();
};

/** Puts a signed-off test on the device and queues its report (nothing captured unless asked). */
async function queued(test: LocalTest, capture: (t: LocalTest) => Promise<Uint8Array> = noCapture) {
  await putTest(test);
  await queueFinalReport(test, NOW, capture);
  await capturesSettled();
}

const pending = async () => Object.fromEntries((await pendingFinalReports()).map((p) => [p.testId, p]));

beforeEach(async () => {
  for (const t of await allTests()) await deleteTest(t.id);
  for (const p of await pendingFinalReports()) await deleteReference(`finalReport:${p.testId}`);
});

describe("capturing the report at sign-off", () => {
  it("keeps the report's own pages from sign-off, and sends those", async () => {
    await queued(signedOff("cap"), async () => CAPTURED);
    expect((await pending()).cap.captured).toEqual(CAPTURED);
    const d = deps();

    expect((await flushFinalReports(d)).sent).toBe(1);

    expect(d.complete).toHaveBeenCalledWith(expect.objectContaining({ id: "cap" }), CAPTURED);
    expect(d.generate).not.toHaveBeenCalled();
    expect(d.put).toHaveBeenCalledWith("cap", CAPTURED);
  });

  it("makes it in full when it's sent if nothing could be captured", async () => {
    await queued(signedOff("nocap"));
    expect((await pending()).nocap.captured).toBeUndefined();
    const d = deps();

    await flushFinalReports(d);

    expect(d.generate).toHaveBeenCalledTimes(1);
    expect(d.put).toHaveBeenCalledWith("nocap", PDF);
  });

  it("is queued before the capture finishes, and a flush waits for the capture rather than regenerating", async () => {
    let finishCapture!: (b: Uint8Array) => void;
    const slowCapture = () => new Promise<Uint8Array>((r) => (finishCapture = r));
    const test = signedOff("slow");
    await putTest(test);
    await queueFinalReport(test, NOW, slowCapture); // resolves with the capture still running
    expect((await pendingFinalReports()).map((p) => p.testId)).toEqual(["slow"]);
    const d = deps();

    const flushing = flushFinalReports(d);
    await new Promise((r) => setTimeout(r, 20));
    expect(d.put).not.toHaveBeenCalled();
    finishCapture(CAPTURED);

    expect((await flushing).sent).toBe(1);
    expect(d.generate).not.toHaveBeenCalled();
    expect(d.put).toHaveBeenCalledWith("slow", CAPTURED);
  });
});

describe("the Final Report upload queue", () => {
  it("sends a queued report once its test is on the server, then forgets it", async () => {
    await queued(signedOff("a"));
    const d = deps();

    const result = await flushFinalReports(d);

    expect(result).toEqual({ sent: 1, waiting: 0, dropped: 0 });
    expect(await pendingFinalReports()).toEqual([]);
  });

  it("waits, without counting it as a failure, while the test itself hasn't gone up", async () => {
    await queued(signedOff("b", { syncState: "local-only", everUploaded: false }));
    const d = deps();

    const result = await flushFinalReports(d);

    expect(result).toEqual({ sent: 0, waiting: 1, dropped: 0 });
    expect(d.generate).not.toHaveBeenCalled();
    expect((await pending()).b).toMatchObject({ attempts: 0 });
    expect((await pending()).b.notBefore).toBeUndefined();
  });

  it("backs off after the server fails it, and tries again once that time has passed", async () => {
    await queued(signedOff("c"));
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

  it("stops at the first sign of no connection, backing that one off — not counted — and leaving the rest", async () => {
    await queued(signedOff("d1"));
    await queued(signedOff("d2"));
    const offline = deps({
      put: vi.fn(async () => {
        throw new TypeError("Failed to fetch");
      }),
    });

    const result = await flushFinalReports(offline);

    expect(offline.put).toHaveBeenCalledTimes(1);
    expect(result).toEqual({ sent: 0, waiting: 2, dropped: 0 });
    const queue = Object.values(await pending());
    expect(queue.map((p) => p.attempts)).toEqual([0, 0]);
    expect(queue.map((p) => p.waits ?? 0).sort()).toEqual([0, 1]);
  });

  it("never gives up for want of signal, however long it lasts", async () => {
    await queued(signedOff("far"));
    let now = NOW.getTime();
    const offline = deps({
      put: vi.fn(async () => {
        throw new TypeError("Failed to fetch");
      }),
      now: () => new Date(now),
    });

    for (let i = 0; i < MAX_ATTEMPTS * 3; i++) {
      await flushFinalReports(offline);
      now += 7 * 3_600_000; // past the longest wait
    }

    expect((await pending()).far).toMatchObject({ attempts: 0, waits: MAX_ATTEMPTS * 3 });
  });

  it("waits for sign-in when the session has lapsed, without holding it against the report", async () => {
    await queued(signedOff("e"));

    const result = await flushFinalReports(deps({ put: vi.fn(async () => new Response(null, { status: 401 })) }));

    expect(result.waiting).toBe(1);
    expect((await pending()).e).toMatchObject({ attempts: 0 });
    expect((await pending()).e.notBefore).toBeUndefined();
  });

  it("gives up on a report the server refuses for good", async () => {
    await queued(signedOff("f"));
    const warn = vi.spyOn(console, "warn").mockImplementation(() => {});

    const result = await flushFinalReports(deps({ put: vi.fn(async () => new Response(null, { status: 413 })) }));

    expect(result).toEqual({ sent: 0, waiting: 0, dropped: 1 });
    expect(await pendingFinalReports()).toEqual([]);
    warn.mockRestore();
  });

  it("never sends one over the server's limit", async () => {
    await queued(signedOff("g"));
    const warn = vi.spyOn(console, "warn").mockImplementation(() => {});
    const huge = deps({ generate: vi.fn(async () => ({ bytes: new Uint8Array(MAX_FINAL_REPORT_BYTES + 1), fileName: "r.pdf", analyser: "none" as const })) });

    const result = await flushFinalReports(huge);

    expect(huge.put).not.toHaveBeenCalled();
    expect(result.dropped).toBe(1);
    warn.mockRestore();
  });

  it("waits — not counted — while the report generator isn't on the device", async () => {
    await queued(signedOff("h"));

    await flushFinalReports(deps({ generate: vi.fn(async () => Promise.reject(new ReportGeneratorUnavailableError())) }));

    const left = (await pending()).h;
    expect(left).toMatchObject({ attempts: 0, waits: 1 });
    expect(left.lastProblem).toContain("generator");
  });

  it("won't send a copy missing the analyser pages the tester's had — it waits for them", async () => {
    await queued(signedOff("i"));
    const d = deps({}, "unreachable");

    await flushFinalReports(d);

    expect(d.put).not.toHaveBeenCalled();
    expect((await pending()).i).toMatchObject({ attempts: 0, waits: 1 });
  });

  it("sends the summary alone when the attachment is one pdf-lib can never read — the tester's was the same", async () => {
    await queued(signedOff("j"));
    expect((await flushFinalReports(deps({}, "unreadable"))).sent).toBe(1);
  });

  it("drops a report whose test is no longer a signed-off test on this device", async () => {
    await queued(signedOff("gone"));
    await deleteTest("gone");
    const warn = vi.spyOn(console, "warn").mockImplementation(() => {});

    expect((await flushFinalReports(deps())).dropped).toBe(1);
    warn.mockRestore();
  });

  it("gives up after the server has refused it so many times", async () => {
    await queued(signedOff("k"));
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
    await queued(signedOff("l"));
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
    await queued(signedOff("m1"));
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
    await queued(signedOff("m2"));
    const second = flushFinalReports(d);
    const third = flushFinalReports(d);
    release();

    expect((await first).sent).toBe(1);
    expect(await second).toEqual(await third);
    expect(d.put.mock.calls.map((c) => c[0]).sort()).toEqual(["m1", "m2"]);
  });
});

describe("retrying by itself", () => {
  afterEach(() => keepRetryingFinalReports({ canTry: () => false, setTimer: () => null, clearTimer: () => {} }));

  // A failed attempt "a wait ago", so the wait has run out by the time the timer fires — the timer's
  // own flush uses the real clock and the real generator and fetch.
  const aWaitAgo = () => new Date(Date.now() - RETRY_MS[0] - 1_000);

  it("tries a backed-off report again when its wait runs out, on a page nobody touches", async () => {
    const timers: { fn: () => void; ms: number }[] = [];
    keepRetryingFinalReports({ canTry: () => true, setTimer: (fn, ms) => timers.push({ fn, ms }), clearTimer: () => {} });
    await queued(signedOff("r1"));
    const realFetch = globalThis.fetch;
    globalThis.fetch = vi.fn(async () => new Response(null, { status: 201 })) as unknown as typeof fetch;
    try {
      await flushFinalReports(deps({ put: vi.fn(async () => new Response(null, { status: 503 })), now: aWaitAgo }));
      expect(timers.at(-1)?.ms).toBe(RETRY_MS[0]);

      timers.at(-1)!.fn();

      await expect.poll(async () => (await pendingFinalReports()).length, { timeout: 20_000 }).toBe(0);
      expect(globalThis.fetch).toHaveBeenCalledWith("/api/sync/final-report/r1", expect.objectContaining({ method: "PUT" }));
    } finally {
      globalThis.fetch = realFetch;
    }
  }, 30_000);

  it("leaves it to the reconnect when the time comes with no connection", async () => {
    const timers: { fn: () => void; ms: number }[] = [];
    let online = true;
    keepRetryingFinalReports({ canTry: () => online, setTimer: (fn, ms) => timers.push({ fn, ms }), clearTimer: () => {} });
    await queued(signedOff("r2"));
    const put = vi.fn(async () => new Response(null, { status: 503 }));
    await flushFinalReports(deps({ put, now: aWaitAgo }));

    online = false;
    const realFetch = globalThis.fetch;
    globalThis.fetch = vi.fn() as unknown as typeof fetch;
    try {
      timers.at(-1)!.fn();
      await new Promise((r) => setTimeout(r, 50));
      expect(globalThis.fetch).not.toHaveBeenCalled();
      expect((await pending()).r2.attempts).toBe(1);
    } finally {
      globalThis.fetch = realFetch;
    }
  });
});

describe("a sync sends the reports queued behind its tests", () => {
  const json = (body: unknown, status = 200) =>
    new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } });
  const realFetch = globalThis.fetch;
  afterEach(() => {
    globalThis.fetch = realFetch;
  });

  it("pushes the signed-off test, then PUTs its captured report as application/pdf", async () => {
    // The real capture: pdfmake lays the report out in Node, the generator already loaded in this
    // "page" (sign-off never downloads it — generatorChunks.reportGeneratorOnDevice).
    await loadPdfMake();
    const test = signedOff("n", { syncState: "local-only", everUploaded: false, testedBy: { name: "Ellie" } });
    await putTest(test);
    await queueFinalReport(test, NOW);
    await capturesSettled();
    const captured = (await pending()).n.captured!;
    expect(Buffer.from(captured.subarray(0, 5)).toString("ascii")).toBe("%PDF-");

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
    expect(puts[0].bytes).toEqual(captured); // no analyser PDF, so the captured pages are the report
    expect((await getTest("n"))?.syncState).toBe("uploaded");
    await expect.poll(async () => (await pendingFinalReports()).length).toBe(0);
  }, 30_000);
});
