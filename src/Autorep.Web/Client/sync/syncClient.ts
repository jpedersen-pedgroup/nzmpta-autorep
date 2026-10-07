// Sync client — the only thing that talks to the server. Pushes local-only tests up
// (POST /api/sync/tests, upsert by ClientId) and pulls the Tester's tests down as a DELTA:
// each pull returns a watermark that is stored and sent back as ?since= on the next one, so
// only tests written since then come down (first pull = full history). Both sides of the
// comparison are the server's clock — device clock skew can't lose tests. The watermark lags
// real time slightly (see SyncController), so recently-written tests are re-delivered on the
// next pull; that's by design and harmless — the loop below skips tests already on-device.
// Auth is the tester's cookie (same-origin fetch sends it automatically).
//
// Reconciliation: a signed-off version whose parent an administrator (or this tester's other
// device) has already replaced is answered 409, with the version both were made from and the one
// already on the server. The device combines them field by field (versioning/merge.ts) and sends
// the pair to /api/sync/tests/merge; every version stays on record. A pull brings an
// administrator's version of a test down like any other (it carries the tester's id), and the
// version it replaces goes read-only through the supersedes link.
import { allTests, currentTesterId, currentTesterName, getTest, putTest, getReference, putReference, storeOwner, type LocalTest } from "../db/testStore";
import { mergeVersions } from "../versioning/merge";
import { defaultMachineConfiguration, type MachineConfiguration } from "../wizard/types";
import { adaptLegacyReadings } from "../report/legacyAdapter";
import { flushCalibration } from "./calibrationSync";
import { initCompanyBranding } from "./companyBrandingSync";
import { initTesterDetails } from "./testerDetailsSync";
import { warmReportGenerator } from "../report/generatorChunks";
import { isBlankRegulatorRow, regulatorRows } from "../wizard/pumpRows";
import { guidesForRoles, TESTER_ROLE, warmGuides } from "../guides/guides";
import { reportSessionOk, reportSignedOut } from "../connectivity";
import { keepHeldBytes, letGoOfHeldAttachments } from "./pulsationAttachment";

interface TestSummaryDto {
  clientId: string;
  farmName: string;
  createdAt: string;
  markedCompleteAt: string | null;
  config: MachineConfiguration | null;
  /** Full offline capture payload (the serialised LocalTest) for exact rehydration. */
  payloadJson: string | null;
}

interface PullResponse {
  /** Server-clock watermark: store it, send it back as ?since= next pull. */
  watermark: string;
  tests: TestSummaryDto[];
  /** Cursor for the next page; absent/null on the last one. */
  next?: string | null;
}

/** Reference-store key for the pull watermark (per-tester DB, so per-tester watermark). */
const WATERMARK_KEY = "testPullWatermark";
/** A pull interrupted part-way: where to resume, and the watermark its first page carried. */
const PROGRESS_KEY = "testPullProgress";
/** Tests per page. Payloads are big (the analyser PDFs stay on the server, but a full capture is
 * still tens of KB), and each page is stored as it lands, so the newest tests appear first. */
export const PULL_PAGE_SIZE = 25;

interface PullProgress {
  /** The `since` this pull started with — progress only resumes the same pull. */
  since: string | null;
  watermark: string;
  cursor: string;
}

export interface SyncResult {
  pushed: number;
  /** Tests whose push failed. The rest of the sync still ran — a stuck test must never wedge it. */
  failed: number;
  pulled: number;
  /** Pushed tests that arrived after another edit of the same test and were combined with it.
   * Present only when there were some. */
  merged?: number;
}

/** The server's answer to a version that arrived second (SyncController.CollisionResponse). */
interface Collision {
  conflict: "superseded";
  baseClientId: string;
  base: TestSummaryDto;
  head: TestSummaryDto;
  headVersion: number;
}

/** A 409's body: a collision to reconcile, or "completed" — the server already holds this version
 * signed off, and a signed-off version never changes in place. */
async function readConflict(res: Response): Promise<{ collision: Collision | null; completed: boolean }> {
  try {
    const body = (await res.json()) as (Partial<Collision> & { error?: string }) | null;
    const collision = body?.conflict === "superseded" && body.base && body.head ? (body as Collision) : null;
    return { collision, completed: body?.error === "completed" };
  } catch {
    return { collision: null, completed: false };
  }
}

/** Thrown when this page's store belongs to a different tester than the one now signed in: the
 * offline shell opened it for whoever the device's record named, and the session check has since
 * said someone else is signed in (the page is about to reload as them). Pushing now would file the
 * first tester's work under the second tester's name — a certified test signed by the wrong person. */
export class StoreOwnerChangedError extends Error {
  constructor() {
    super("Signed-in tester changed");
    this.name = "StoreOwnerChangedError";
  }
}

/** Thrown when the server answered, but as "you are not signed in" rather than as the API.
 * Distinct from an unreachable server: the fix is signing in again, not waiting for signal. */
export class SessionExpiredError extends Error {
  constructor() {
    super("Session expired");
    this.name = "SessionExpiredError";
  }
}

/**
 * Guards against an authentication redirect being read as success. The server now answers 401 on
 * /api (see Program.cs), but `redirect: "manual"` means that even a misconfigured or proxied
 * redirect surfaces as an opaque response instead of `fetch` silently following it to a 200 HTML
 * login page — which would otherwise mark a test uploaded that the server never received.
 */
function assertApiResponse(res: Response): void {
  if (res.status === 401 || res.status === 403 || res.type === "opaqueredirect" || res.redirected) {
    // The header indicator should say so too, not keep showing "online".
    reportSignedOut();
    throw new SessionExpiredError();
  }
}

/** The config as the server stores it. Regulators always go up as a list (never absent), so the
 * server can tell this client knows about them and stores the suitability answer with them - an
 * older test with no list sends the lines read from its pump rows. */
function configForServer(config: MachineConfiguration): MachineConfiguration {
  return {
    ...config,
    regulators: regulatorRows(config).filter((r) => !isBlankRegulatorRow(r)),
    regulatorsSuitable: config.regulatorsSuitable ?? null,
  };
}

/** A test as the push (and the merge) sends it. */
function pushBody(t: LocalTest) {
  return {
    clientId: t.id,
    farmName: t.farmName,
    // Farm identity so the server links the right farm within the tester's company scope
    // (id from the picker, plus supply number + milk processor to disambiguate same names).
    farmId: t.farmId ?? null,
    farmSupplyNumber: t.farm?.supplyNumber ?? null,
    farmMilkCompanyName: t.farm?.milkCompanyName ?? null,
    notes: t.notes ?? null,
    markedCompleteAt: t.markedCompleteAt ?? null,
    createdAt: t.createdAt,
    config: t.config ? configForServer(t.config) : t.config,
    // Version chain, mirrored out of the payload into its own columns so the server can hide
    // superseded versions from the company-wide list without parsing (and loading) the payload.
    version: t.version ?? 1,
    supersedesClientId: t.supersedesId ?? null,
    mergedFromClientId: t.mergedFromId ?? null,
    // Mirrored into its own column for the admin Upcoming tests page.
    nextTestDate: t.nextTestDate ?? null,
    // The full rich capture round-trips as JSON so a re-download rehydrates exactly.
    payloadJson: JSON.stringify(t),
  };
}

/** Sends one test. Resolves true when it had to be combined with another edit of the same test. */
async function pushTest(t: LocalTest): Promise<boolean> {
  const res = await fetch("/api/sync/tests", {
    method: "POST",
    redirect: "manual",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(pushBody(t)),
  });
  assertApiResponse(res);
  if (res.status === 409) {
    const { collision, completed } = await readConflict(res);
    if (collision) {
      await reconcile(t, collision);
      return true;
    }
    if (completed) {
      // The server's signed-off copy stands; this one goes clean and the pull refreshes it from there.
      await markSent(t);
      return false;
    }
  }
  if (!res.ok) throw new Error(`Push failed (${res.status})`);
  await markSent(t);
  return false;
}

/**
 * Combines a signed-off version the server answered 409 with the test's current version, and sends
 * the pair. If the test moves on again meanwhile (another administrator edit), the server answers
 * with the new head and the combine is redone against it — a few times at most.
 */
async function reconcile(t: LocalTest, first: Collision): Promise<void> {
  let collision = first;
  for (let attempt = 0; attempt < 3; attempt++) {
    const base = localFromSummary(collision.base);
    const head = localFromSummary(collision.head);
    const { merged } = mergeVersions(base, head, t, {
      id: crypto.randomUUID(),
      now: new Date().toISOString(),
      mergedBy: currentTesterName() ?? null,
    });
    const res = await fetch("/api/sync/tests/merge", {
      method: "POST",
      redirect: "manual",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ incoming: pushBody(t), merged: pushBody(merged), headClientId: head.id }),
    });
    assertApiResponse(res);
    if (res.status === 409) {
      const next = (await readConflict(res)).collision;
      if (next) {
        collision = next;
        continue;
      }
    }
    if (!res.ok) throw new Error(`Merge failed (${res.status})`);
    const answer = (await res.json().catch(() => null)) as { status?: string } | null;
    if (answer?.status === "merged") {
      // What the server now holds: the head (if this device didn't have it) and the combined version.
      if (!(await getTest(head.id))) await putTest(head);
      await putTest({ ...merged, syncState: "uploaded", everUploaded: true });
    }
    await markSent(t);
    return;
  }
  throw new Error("The test kept changing on the server while it was being combined");
}

/**
 * Marks what was SENT as uploaded — not whatever is on the device now. The push took a network
 * round trip, and the test may have been edited meanwhile (the wizard saves on every keystroke, and
 * a sync can start on its own when the connection returns). Writing the sent snapshot back would
 * silently undo that edit; marking the edited copy "uploaded" would mean it never gets sent.
 */
async function markSent(sent: LocalTest): Promise<void> {
  const now = await getTest(sent.id);
  if (!now) return; // deleted meanwhile: the server's copy comes back on the next pull
  if (now.syncState === "local-only" && now.updatedAt === sent.updatedAt) {
    await putTest({ ...now, syncState: "uploaded", everUploaded: true });
  } else if (!now.everUploaded) {
    // Edited during the push: the server has an earlier copy, and the edit goes up next time.
    await putTest({ ...now, everUploaded: true });
  }
}

/**
 * Pulls the tester's tests in pages, newest first, storing each page as it lands. The first pull
 * on a device is the tester's whole history; paging means it shows the newest tests while the tail
 * is still arriving, and an interruption resumes from the last stored page instead of starting
 * again. The watermark kept is the FIRST page's, so anything written while paging comes back on
 * the next pull (see SyncController.ListTests). The analyser PDFs' bytes stay on the server.
 */
async function pullTests(): Promise<number> {
  let since: string | null = null;
  try {
    since = (await getReference(WATERMARK_KEY))?.version ?? null;
  } catch {
    // No watermark readable — fall through to a full pull.
  }

  let progress: PullProgress | null = null;
  try {
    const saved = (await getReference(PROGRESS_KEY))?.rows as PullProgress | null | undefined;
    if (saved && typeof saved.cursor === "string" && saved.since === since) progress = saved;
  } catch {
    // Unreadable — start from the top.
  }

  let cursor = progress?.cursor ?? null;
  let watermark = progress?.watermark ?? null;
  let restarted = false;
  let added = 0;
  for (;;) {
    const params = new URLSearchParams({ limit: String(PULL_PAGE_SIZE), attachments: "omit" });
    if (since) params.set("since", since);
    if (cursor) params.set("cursor", cursor);
    const res = await fetch(`/api/sync/tests?${params}`, { headers: { Accept: "application/json" }, redirect: "manual" });
    assertApiResponse(res);
    if (res.status === 400 && cursor && !restarted) {
      // The server doesn't recognise the saved cursor — start this pull again from the top.
      restarted = true;
      cursor = null;
      watermark = null;
      continue;
    }
    if (!res.ok) throw new Error(`Pull failed (${res.status})`);
    const page = (await res.json()) as PullResponse;
    watermark ??= page.watermark;
    added += await storePulled(Array.isArray(page.tests) ? page.tests : []);
    if (!page.next) break;
    cursor = page.next;
    await putReference({ key: PROGRESS_KEY, rows: { since, watermark, cursor } satisfies PullProgress });
  }

  // Advance the watermark only after every page is stored: an interrupted pull re-fetches from
  // its saved progress next time (safe — the loop upserts) instead of losing the tail.
  await putReference({ key: WATERMARK_KEY, version: watermark });
  await putReference({ key: PROGRESS_KEY, rows: null });
  return added;
}

/** A test the server sent (a pull, or a collision answer) as this device holds it: clean, since the
 * server has it. */
function localFromSummary(r: TestSummaryDto): LocalTest {
  const now = new Date().toISOString();
  // Prefer the full payload (exact rehydration); fall back to the header for older tests.
  if (r.payloadJson) {
    try {
      const parsed = JSON.parse(r.payloadJson) as Record<string, unknown>;
      if (parsed.legacy !== undefined && parsed.currentStep === undefined) {
        // MIGRATED legacy test: the payload is raw legacy columns, not a LocalTest. Adapt it into
        // a read-only LocalTest carrying the original (as-recorded) pass/fail verdicts.
        const adapted = adaptLegacyReadings(parsed);
        return {
          id: r.clientId,
          farmName: r.farmName,
          config: r.config ?? defaultMachineConfiguration(),
          currentStep: "Setup",
          visualFaults: {},
          attestations: [],
          readings: adapted.readings,
          verdicts: adapted.verdicts,
          recordedRecommendations: adapted.recordedRecommendations,
          recordedVisualFaults: adapted.recordedVisualFaults,
          clusterRows: adapted.clusterRows,
          calAirFlowMeters: adapted.calAirFlowMeters,
          calPulsatorTesters: adapted.calPulsatorTesters,
          calVacuumGauges: adapted.calVacuumGauges,
          recommendations: {},
          dataFields: {},
          notes: adapted.comment,
          createdAt: r.createdAt,
          updatedAt: now,
          markedCompleteAt: r.markedCompleteAt,
          syncState: "uploaded",
          everUploaded: true,
          readonly: true,
        };
      }
      // New-format test: the payload IS a serialised LocalTest — rehydrate exactly.
      return { ...(parsed as unknown as LocalTest), id: r.clientId, syncState: "uploaded", everUploaded: true };
    } catch {
      // Unreadable payload — fall through to the header.
    }
  }
  return {
    id: r.clientId,
    farmName: r.farmName,
    config: r.config ?? defaultMachineConfiguration(),
    currentStep: "Setup",
    visualFaults: {},
    attestations: [],
    readings: {},
    recommendations: {},
    dataFields: {},
    createdAt: r.createdAt,
    updatedAt: now,
    markedCompleteAt: r.markedCompleteAt,
    syncState: "uploaded",
    everUploaded: true,
  };
}

async function storePulled(remote: readonly TestSummaryDto[]): Promise<number> {
  let added = 0;
  for (const r of remote) {
    // A DIRTY local copy wins (it holds edits the server hasn't seen — they'll push next).
    // A CLEAN ("uploaded") copy is by definition one the server has seen, so the server's
    // current state replaces it — otherwise a device that pulled an in-progress draft would
    // keep it stale forever and never receive the completed version or its amendment history.
    const existing = await getTest(r.clientId);
    if (existing && existing.syncState !== "uploaded") continue;

    // The server leaves analyser PDFs' bytes behind; keep this device's copy if it holds one.
    await putTest(keepHeldBytes(existing, localFromSummary(r)));
    added++;
  }
  return added;
}

/** Dispatched on window when a sync starts and finishes (`detail.running`). */
export const SYNC_STATE_EVENT = "autorep:sync-state";

let running: Promise<SyncResult> | null = null;
let queued: Promise<SyncResult> | null = null;

function announceSync(isRunning: boolean): void {
  const target = globalThis as { dispatchEvent?: (e: Event) => boolean };
  if (typeof target.dispatchEvent === "function" && typeof CustomEvent === "function") {
    target.dispatchEvent(new CustomEvent(SYNC_STATE_EVENT, { detail: { running: isRunning } }));
  }
}

/**
 * Push every local-only test, then pull the Tester's tests down. Also flushes a pending
 * offline edit of the tester's calibration dates (kept dirty until the server accepts it) and
 * re-checks the company branding for the report letterhead (a 304 unless an admin changed it) and
 * the tester's own details the report names.
 *
 * One at a time: syncs can now start on their own (sync/autoSync.ts) as well as from Sync now and
 * sign-off. A call made while one is running gets ONE more run after it — the running one may have
 * read the queue before the caller's latest change (a test just marked complete) — and every call
 * made meanwhile shares that same follow-up run.
 */
export function syncAll(): Promise<SyncResult> {
  if (!running) {
    announceSync(true);
    running = runSync().finally(() => {
      running = null;
      announceSync(false);
    });
    return running;
  }
  queued ??= running
    .catch(() => undefined)
    .then(() => {
      queued = null;
      return syncAll();
    });
  return queued;
}

async function runSync(): Promise<SyncResult> {
  const owner = storeOwner();
  if (owner !== undefined && owner !== currentTesterId()) throw new StoreOwnerChangedError();
  await flushCalibration();
  await initCompanyBranding();
  await initTesterDetails();
  const locals = await allTests();
  let pushed = 0;
  let failed = 0;
  let merged = 0;
  for (const t of locals) {
    if (t.syncState !== "local-only") continue;
    try {
      if (await pushTest(t)) merged++;
      pushed++;
    } catch (e) {
      // One test the server won't take (oversized payload, validation, a transient 5xx) must not
      // block the tests behind it or the pull — after an offline day the queue is long and this
      // is exactly when a bad one shows up. An expired session is different: nothing else will
      // succeed either, so stop and let the caller prompt for sign-in.
      if (e instanceof SessionExpiredError) throw e;
      failed++;
    }
  }
  const pulled = await pullTests();
  // The server just accepted this session end to end.
  reportSessionOk();
  // Now that the server is known to hold them, let go of analyser PDFs this device has kept
  // long enough (sync/pulsationAttachment.ts). Best-effort and quick: no network involved.
  await letGoOfHeldAttachments(await allTests()).catch(() => 0);

  // A sync just succeeded, so the connection is real and the tester is almost certainly not
  // stuck in a paddock. That is the moment to pull down the report generator's lazy chunks, so
  // printing works on-farm later on a device that has never printed before. Deliberately not
  // awaited: it is ~2.4 MB and no one should wait on it to see their tests.
  void warmReportGenerator();
  // Same moment, same reasoning, for the tester's work instructions (a few MB, a 304 once held):
  // the Help page can't open offline, but the guide links in the tester app can — from this copy.
  void warmGuides(guidesForRoles([TESTER_ROLE]));

  return merged > 0 ? { pushed, failed, pulled, merged } : { pushed, failed, pulled };
}
