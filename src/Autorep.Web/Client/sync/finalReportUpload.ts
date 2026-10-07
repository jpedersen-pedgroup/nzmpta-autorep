// The Final Report as signed off goes to the server, so an administrator can download what the
// farmer was given rather than a regeneration (PRD: FinalReportBlob; FinalReportsController).
//
// Sign-off queues the test (queueFinalReport). Each sync then sends what is queued, once the test
// itself is on the server: the device generates the full report again — every part, the analyser
// PDF's pages on the end, pinned to the moment of sign-off (finalReportPdf) — and PUTs the bytes.
// Generating again rather than keeping the bytes from sign-off: the generator gives the same bytes
// for the same test, and an iPad's storage is too tight to hold a second copy of a 15 MB analyser
// PDF until the signal comes back.
//
// Nothing here can get in the way of the tester: printing never depends on it, the test's own sync
// doesn't wait for it, and a report that can't go yet (no signal, the generator's chunks or the
// analyser's bytes not on the device yet, the server busy) waits — backing off — for a later sync.
import {
  currentTesterId,
  deleteReference,
  getTest,
  putReference,
  referencesWithPrefix,
  storeOwner,
  type LocalTest,
} from "../db/testStore";
import { finalReportPdf, type ReportPdf } from "../report/testSummaryPdf";
import { ReportGeneratorUnavailableError } from "../report/generatorChunks";
import { fetchWithTimeout, reportSignedOut } from "../connectivity";

const KEY_PREFIX = "finalReport:";

/** The server's limit (FinalReportsController.MaxBytes): a report past it is never sent. */
export const MAX_FINAL_REPORT_BYTES = 25 * 1024 * 1024;

/** Waits after each failed attempt, in order; the last repeats. */
export const RETRY_MS = [60_000, 5 * 60_000, 15 * 60_000, 60 * 60_000, 6 * 60 * 60_000];

/** Failed attempts after which a report is given up on. The test itself is on the server either
 * way; this only stops a report the server keeps refusing from being regenerated for ever. */
export const MAX_ATTEMPTS = 12;

/** A big PDF on one bar of signal takes a while; past this it's treated as no connection. */
const UPLOAD_TIMEOUT_MS = 5 * 60_000;

export interface PendingFinalReport {
  testId: string;
  queuedAt: string;
  /** Attempts that failed so far. */
  attempts: number;
  /** Not to be tried again before this (ISO); absent when due now. */
  notBefore?: string;
  /** Why the last attempt didn't go — for anyone looking at the device's storage. */
  lastProblem?: string;
}

/** Queues the test's Final Report for upload. Called at sign-off, before the sync that sends the
 * test, so a report signed off with no signal still goes when the signal returns. */
export async function queueFinalReport(testId: string, now = new Date()): Promise<void> {
  const entry: PendingFinalReport = { testId, queuedAt: now.toISOString(), attempts: 0 };
  await putReference({ key: KEY_PREFIX + testId, rows: entry });
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
  generate(test: LocalTest): Promise<ReportPdf>;
  put(testId: string, bytes: Uint8Array): Promise<Response>;
  now(): Date;
}

const defaultDeps: FlushDeps = {
  generate: finalReportPdf,
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
  /** Not ready to go: no attempt counted. */
  | { kind: "wait" }
  /** This one failed: back off, carry on with the rest. */
  | { kind: "retry"; problem: string }
  /** No connection: back off this one and stop — nothing else will go either. */
  | { kind: "offline"; problem: string }
  /** Signed out: stop, without counting it against the report. */
  | { kind: "signed-out" };

async function attempt(item: PendingFinalReport, deps: FlushDeps): Promise<Outcome> {
  const test = await getTest(item.testId);
  // Deleted from the device, or somehow not a signed-off test: there's no report to send.
  if (!test || !test.markedCompleteAt) return { kind: "drop", problem: "no signed-off test on this device" };
  // The server takes a report only for a test it already holds as complete, so the test goes first.
  if (test.syncState !== "uploaded" || !test.everUploaded) return { kind: "wait" };

  let pdf: ReportPdf;
  try {
    pdf = await deps.generate(test);
  } catch (e) {
    return {
      kind: "retry",
      problem: e instanceof ReportGeneratorUnavailableError ? "the report generator isn't on this device yet" : "the report couldn't be generated",
    };
  }
  // The tester's report had the analyser's pages; a copy without them isn't "as signed off".
  // (An attachment pdf-lib can't read is different: it never could, so the tester's report didn't
  // have them either, and the copy without them is the true one.)
  if (pdf.analyser === "unreachable" || pdf.analyser === "merger-unavailable") {
    return { kind: "retry", problem: "the analyser PDF isn't on this device yet" };
  }
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
  return { kind: "retry", problem: `the server answered ${res.status}` };
}

async function backOff(item: PendingFinalReport, problem: string, now: Date): Promise<boolean> {
  const attempts = item.attempts + 1;
  if (attempts >= MAX_ATTEMPTS) return false;
  const wait = RETRY_MS[Math.min(attempts - 1, RETRY_MS.length - 1)];
  const next: PendingFinalReport = { ...item, attempts, notBefore: new Date(now.getTime() + wait).toISOString(), lastProblem: problem };
  await putReference({ key: KEY_PREFIX + item.testId, rows: next });
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
      outcome = { kind: "retry", problem: "unexpected error" };
    }
    try {
      switch (outcome.kind) {
        case "sent":
          await deleteReference(KEY_PREFIX + item.testId);
          result.sent++;
          break;
        case "drop":
          await deleteReference(KEY_PREFIX + item.testId);
          result.dropped++;
          console.warn(`Final Report for test ${item.testId} not sent: ${outcome.problem}.`);
          break;
        case "wait":
          result.waiting++;
          break;
        case "retry":
        case "offline":
          if (await backOff(item, outcome.problem, now)) {
            result.waiting++;
          } else {
            await deleteReference(KEY_PREFIX + item.testId);
            result.dropped++;
            console.warn(`Final Report for test ${item.testId} given up after ${MAX_ATTEMPTS} attempts: ${outcome.problem}.`);
          }
          break;
        case "signed-out":
          result.waiting++;
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
  return result;
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
