// The Final Report as signed off goes to the server, so an administrator can download what the
// farmer was given rather than a regeneration (PRD: FinalReportBlob; FinalReportsController).
//
// At sign-off the device queues the test and CAPTURES the report: its own pages, laid out there
// and then from the standards, letterhead and privacy footer it holds at that moment and pinned to
// the sign-off time (captureFinalReport). Those inputs can change before the upload — a standards
// update arriving with the next sync would otherwise recompute pass/fail in the "as signed off"
// copy. What isn't captured is the analyser PDF's pages: the PDF is up to 15 MB, an iPad's storage
// is tight, and its bytes don't change (the device keeps them, or the server does), so they're
// appended when the report is sent (completeFinalReport) — byte for byte as the full report would
// have had them. If the capture couldn't run (the generator's chunks weren't on the device yet),
// the report is made in full when it's sent instead, from what the device holds then.
//
// Each sync then sends what is queued once the test itself is on the server. Nothing here can get
// in the way of the tester: printing never depends on it, the sync doesn't wait for it, and a
// report that can't go yet waits — backing off — for a later attempt. Only the server refusing it
// counts towards giving up; no signal, or a generator or analyser PDF not on the device yet, never
// does.
import {
  FINAL_REPORT_KEY_PREFIX as KEY_PREFIX,
  currentTesterId,
  deleteReference,
  getReference,
  getTest,
  putReference,
  referencesWithPrefix,
  storeOwner,
  type LocalTest,
} from "../db/testStore";
import { captureFinalReport, completeFinalReport, finalReportPdf, type ReportPdf } from "../report/testSummaryPdf";
import { ReportGeneratorUnavailableError, reportGeneratorOnDevice } from "../report/generatorChunks";
import { fetchWithTimeout, reportSignedOut } from "../connectivity";

/** The server's limit (FinalReportsController.MaxBytes): a report past it is never sent. */
export const MAX_FINAL_REPORT_BYTES = 25 * 1024 * 1024;

/** Waits after each failed attempt, in order; the last repeats. */
export const RETRY_MS = [60_000, 5 * 60_000, 15 * 60_000, 60 * 60_000, 6 * 60 * 60_000];

/** Times the server can refuse a report before it is given up on. The test itself is on the server
 * either way; this only stops a report the server keeps refusing from being sent for ever. */
export const MAX_ATTEMPTS = 12;

/** A big PDF on one bar of signal takes a while; past this it's treated as no connection. */
const UPLOAD_TIMEOUT_MS = 5 * 60_000;

export interface PendingFinalReport {
  testId: string;
  queuedAt: string;
  /** The report's own pages as captured at sign-off (captureFinalReport); absent if that couldn't
   * run, and the report is then made in full when it's sent. */
  captured?: Uint8Array;
  /** Times the server refused or failed it. */
  attempts: number;
  /** Times it couldn't go for want of something on this side — signal, the generator's chunks,
   * the analyser PDF's bytes. Spaces the retries out; never gives up. Reset by any server answer. */
  waits?: number;
  /** Not to be tried again before this (ISO); absent when due now. */
  notBefore?: string;
  /** Why the last attempt didn't go — for anyone looking at the device's storage. */
  lastProblem?: string;
}

const keyFor = (testId: string) => KEY_PREFIX + testId;

/** Captures still being laid out, by test: a flush waits for its test's capture rather than
 * regenerating the report from inputs that may have moved on. */
const capturing = new Map<string, Promise<void>>();

/** The capture at sign-off — only when the generator is already on the device. Sign-off never
 * downloads it (it may be the moment the signal is weakest), and an import tried with no signal
 * would break the generator for the rest of the page (generatorChunks.reportGeneratorOnDevice). */
async function captureIfGeneratorOnDevice(test: LocalTest): Promise<Uint8Array> {
  if (!(await reportGeneratorOnDevice())) throw new ReportGeneratorUnavailableError();
  return captureFinalReport(test);
}

/**
 * Queues the signed-off test's Final Report and captures it (see the top of this file). Resolves as
 * soon as the report is queued — before the capture, which carries on in the background — so
 * sign-off and its sync never wait on laying out a report. Called at sign-off, before that sync, so
 * a report signed off with no signal still goes when the signal returns.
 */
export async function queueFinalReport(
  test: LocalTest,
  now = new Date(),
  capture: (test: LocalTest) => Promise<Uint8Array> = captureIfGeneratorOnDevice,
): Promise<void> {
  const key = keyFor(test.id);
  const entry: PendingFinalReport = { testId: test.id, queuedAt: now.toISOString(), attempts: 0 };
  await putReference({ key, rows: entry });
  const job = (async () => {
    try {
      const captured = await capture(test);
      const current = (await getReference(key))?.rows as PendingFinalReport | undefined;
      if (current) await putReference({ key, rows: { ...current, captured } satisfies PendingFinalReport });
    } catch {
      // Not captured — the generator's chunks aren't on this device yet, or storage is full. The
      // report is made in full when it's sent instead.
    }
  })();
  capturing.set(test.id, job);
  void job.finally(() => {
    if (capturing.get(test.id) === job) capturing.delete(test.id);
  });
}

/** Waits for any capture still running (tests, and a flush racing a sign-off). */
export async function capturesSettled(): Promise<void> {
  await Promise.all(capturing.values());
}

export async function pendingFinalReports(): Promise<PendingFinalReport[]> {
  return (await referencesWithPrefix(KEY_PREFIX))
    .map((e) => e.rows as PendingFinalReport | null | undefined)
    .filter((p): p is PendingFinalReport => !!p && typeof p.testId === "string");
}

/** Queued reports due to be tried now. */
export async function dueFinalReports(now = new Date()): Promise<number> {
  return (await pendingFinalReports()).filter((p) => isDue(p, now)).length;
}

function isDue(p: PendingFinalReport, now: Date): boolean {
  return !p.notBefore || Date.parse(p.notBefore) <= now.getTime();
}

export interface FlushResult {
  /** Reports the server now holds. */
  sent: number;
  /** Still queued: not due, not ready (the test hasn't gone up yet), or failed and backing off. */
  waiting: number;
  /** Given up on: refused by the server for good, too large, or no longer a completed test here. */
  dropped: number;
}

export interface FlushDeps {
  /** The full report made now (no capture). */
  generate(test: LocalTest): Promise<ReportPdf>;
  /** The full report from the pages captured at sign-off. */
  complete(test: LocalTest, captured: Uint8Array): Promise<ReportPdf>;
  put(testId: string, bytes: Uint8Array): Promise<Response>;
  now(): Date;
}

const defaultDeps: FlushDeps = {
  generate: finalReportPdf,
  complete: completeFinalReport,
  put: (testId, bytes) =>
    fetchWithTimeout(
      `/api/sync/final-report/${encodeURIComponent(testId)}`,
      {
        method: "PUT",
        // A redirect would be the sign-in page — never follow it and call that success.
        redirect: "manual",
        headers: { "Content-Type": "application/pdf" },
        body: new Blob([bytes as BlobPart], { type: "application/pdf" }),
      },
      UPLOAD_TIMEOUT_MS,
    ),
  now: () => new Date(),
};

type Outcome =
  | { kind: "sent" }
  | { kind: "drop"; problem: string }
  /** The test hasn't gone up yet: try at the next flush, no wait. */
  | { kind: "not-yet" }
  /** The server refused or failed it: back off, counted towards giving up. */
  | { kind: "refused"; problem: string }
  /** Something on this side isn't ready: back off, never counted. */
  | { kind: "wait"; problem: string }
  /** No connection: back off this one (not counted) and stop — nothing else will go either. */
  | { kind: "offline"; problem: string }
  /** Signed out: stop, and leave the report as it was. */
  | { kind: "signed-out" };

async function attempt(item: PendingFinalReport, deps: FlushDeps): Promise<Outcome> {
  const test = await getTest(item.testId);
  // Deleted from the device, or somehow not a signed-off test: there's no report to send.
  if (!test || !test.markedCompleteAt) return { kind: "drop", problem: "no signed-off test on this device" };
  // The server takes a report only for a test it already holds as complete, so the test goes first.
  if (test.syncState !== "uploaded" || !test.everUploaded) return { kind: "not-yet" };

  // A capture still being laid out (this page signed it off a moment ago) is what gets sent.
  await capturing.get(item.testId);
  const captured = ((await getReference(keyFor(item.testId)))?.rows as PendingFinalReport | undefined)?.captured;

  let pdf: ReportPdf;
  try {
    pdf = captured ? await deps.complete(test, captured) : await deps.generate(test);
  } catch (e) {
    return e instanceof ReportGeneratorUnavailableError
      ? { kind: "wait", problem: "the report generator isn't on this device yet" }
      : { kind: "refused", problem: "the report couldn't be generated" };
  }
  // The tester's report had the analyser's pages; a copy without them isn't "as signed off".
  // (An attachment pdf-lib can't read is different: it never could, so the tester's report didn't
  // have them either, and the copy without them is the true one.)
  if (pdf.analyser === "unreachable") return { kind: "wait", problem: "the analyser PDF's bytes are on the server and couldn't be fetched" };
  if (pdf.analyser === "merger-unavailable") return { kind: "wait", problem: "the PDF merger isn't on this device yet" };
  if (pdf.bytes.length > MAX_FINAL_REPORT_BYTES) return { kind: "drop", problem: `too large (${pdf.bytes.length} bytes)` };

  let res: Response;
  try {
    res = await deps.put(test.id, pdf.bytes);
  } catch {
    return { kind: "offline", problem: "no connection" };
  }
  if (res.status === 401 || res.status === 403 || res.type === "opaqueredirect" || res.redirected) {
    reportSignedOut();
    return { kind: "signed-out" };
  }
  if (res.ok) return { kind: "sent" };
  // Refused for good: not a PDF (400), not application/pdf (415), too large (413).
  if (res.status === 400 || res.status === 413 || res.status === 415) return { kind: "drop", problem: `refused (${res.status})` };
  // 404/409 (the server hasn't got the completed test — yet), 5xx (the store is down): later.
  return { kind: "refused", problem: `the server answered ${res.status}` };
}

const waitAfter = (failures: number) => RETRY_MS[Math.min(Math.max(failures, 1) - 1, RETRY_MS.length - 1)];

/** Writes the backed-off entry; false when it has been refused too often and is given up on. */
async function backOff(item: PendingFinalReport, outcome: { kind: "refused" | "wait" | "offline"; problem: string }, now: Date): Promise<boolean> {
  // Re-read: a capture may have landed since the flush listed the queue.
  const current = ((await getReference(keyFor(item.testId)))?.rows as PendingFinalReport | undefined) ?? item;
  let next: PendingFinalReport;
  if (outcome.kind === "refused") {
    const attempts = current.attempts + 1;
    if (attempts >= MAX_ATTEMPTS) return false;
    next = { ...current, attempts, waits: 0, notBefore: new Date(now.getTime() + waitAfter(attempts)).toISOString(), lastProblem: outcome.problem };
  } else {
    const waits = (current.waits ?? 0) + 1;
    next = { ...current, waits, notBefore: new Date(now.getTime() + waitAfter(waits)).toISOString(), lastProblem: outcome.problem };
  }
  await putReference({ key: keyFor(item.testId), rows: next });
  return true;
}

async function runFlush(deps: FlushDeps): Promise<FlushResult> {
  const result: FlushResult = { sent: 0, waiting: 0, dropped: 0 };
  // The same guard as the sync (syncClient.runSync): this page's store may belong to the tester the
  // device last knew, not the one now signed in. Their report must not go under someone else's name.
  const owner = storeOwner();
  if (owner !== undefined && owner !== currentTesterId()) return result;

  let pending: PendingFinalReport[];
  try {
    pending = await pendingFinalReports();
  } catch {
    return result;
  }
  const now = deps.now();
  for (let i = 0; i < pending.length; i++) {
    const item = pending[i];
    if (!isDue(item, now)) {
      result.waiting++;
      continue;
    }
    let outcome: Outcome;
    try {
      outcome = await attempt(item, deps);
    } catch {
      outcome = { kind: "refused", problem: "unexpected error" };
    }
    try {
      switch (outcome.kind) {
        case "sent":
          await deleteReference(keyFor(item.testId));
          result.sent++;
          break;
        case "drop":
          await deleteReference(keyFor(item.testId));
          result.dropped++;
          console.warn(`Final Report for test ${item.testId} not sent: ${outcome.problem}.`);
          break;
        case "not-yet":
        case "signed-out":
          result.waiting++;
          break;
        case "refused":
        case "wait":
        case "offline":
          if (await backOff(item, outcome, now)) {
            result.waiting++;
          } else {
            await deleteReference(keyFor(item.testId));
            result.dropped++;
            console.warn(`Final Report for test ${item.testId} given up after ${MAX_ATTEMPTS} refusals: ${outcome.problem}.`);
          }
          break;
      }
    } catch {
      // The queue couldn't be written (storage trouble): it's tried again as it stands next time.
      result.waiting++;
    }
    if (outcome.kind === "offline" || outcome.kind === "signed-out") {
      result.waiting += pending.length - i - 1;
      break;
    }
  }
  await scheduleNextAttempt(now);
  return result;
}

export interface RetryTimer {
  /** Whether it's worth trying at all right now (the device thinks it's online). */
  canTry: () => boolean;
  setTimer: (fn: () => void, ms: number) => unknown;
  clearTimer: (handle: unknown) => void;
}

let retryTimer: RetryTimer | null = null;
let retryHandle: unknown = null;

/**
 * Keeps retrying, by itself, a queued report whose wait has run out — for a page left open with a
 * steady connection, where no reconnect, focus or reload comes along to try again. Started once per
 * page by sync/autoSync.ts (tester pages). When the time comes with no connection, it leaves it to
 * the reconnect.
 */
export function keepRetryingFinalReports(timer: RetryTimer): void {
  retryTimer = timer;
}

async function scheduleNextAttempt(now: Date): Promise<void> {
  const timer = retryTimer;
  if (!timer) return;
  if (retryHandle !== null) timer.clearTimer(retryHandle);
  retryHandle = null;
  let next = Infinity;
  try {
    for (const p of await pendingFinalReports()) {
      if (p.notBefore) next = Math.min(next, Date.parse(p.notBefore));
    }
  } catch {
    return;
  }
  if (!Number.isFinite(next)) return;
  retryHandle = timer.setTimer(() => {
    retryHandle = null;
    if (timer.canTry()) void flushFinalReports();
  }, Math.max(1_000, next - now.getTime()));
}

let running: Promise<FlushResult> | null = null;
let queued: Promise<FlushResult> | null = null;

/**
 * Sends the queued reports that are due. Never throws. One flush at a time per page: a call made
 * while one runs gets one more run afterwards (it may have queued a report the running one didn't
 * read), shared by every caller meanwhile — the same arrangement as syncAll.
 */
export function flushFinalReports(deps: FlushDeps = defaultDeps): Promise<FlushResult> {
  if (!running) {
    running = runFlush(deps)
      .catch((): FlushResult => ({ sent: 0, waiting: 0, dropped: 0 }))
      .finally(() => {
        running = null;
      });
    return running;
  }
  queued ??= running.then(() => {
    queued = null;
    return flushFinalReports(deps);
  });
  return queued;
}
