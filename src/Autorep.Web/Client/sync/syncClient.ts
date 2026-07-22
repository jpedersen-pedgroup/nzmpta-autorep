// Sync client — the only thing that talks to the server. Pushes local-only tests up
// (POST /api/sync/tests, upsert by ClientId) and pulls the Tester's tests down as a DELTA:
// each pull returns a watermark that is stored and sent back as ?since= on the next one, so
// only tests written since then come down (first pull = full history). Both sides of the
// comparison are the server's clock — device clock skew can't lose tests. The watermark lags
// real time slightly (see SyncController), so recently-written tests are re-delivered on the
// next pull; that's by design and harmless — the loop below skips tests already on-device.
// Auth is the tester's cookie (same-origin fetch sends it automatically).
import { allTests, getTest, putTest, getReference, putReference, type LocalTest } from "../db/testStore";
import { defaultMachineConfiguration, type MachineConfiguration } from "../wizard/types";
import { adaptLegacyReadings } from "../report/legacyAdapter";
import { flushCalibration } from "./calibrationSync";
import { warmReportGenerator } from "../report/generatorChunks";

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
}

/** Reference-store key for the pull watermark (per-tester DB, so per-tester watermark). */
const WATERMARK_KEY = "testPullWatermark";

export interface SyncResult {
  pushed: number;
  /** Tests whose push failed. The rest of the sync still ran — a stuck test must never wedge it. */
  failed: number;
  pulled: number;
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
    throw new SessionExpiredError();
  }
}

async function pushTest(t: LocalTest): Promise<void> {
  const res = await fetch("/api/sync/tests", {
    method: "POST",
    redirect: "manual",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
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
      config: t.config,
      // Version chain, mirrored out of the payload into its own columns so the server can hide
      // superseded versions from the company-wide list without parsing (and loading) the payload.
      version: t.version ?? 1,
      supersedesClientId: t.supersedesId ?? null,
      // The full rich capture round-trips as JSON so a re-download rehydrates exactly.
      payloadJson: JSON.stringify(t),
    }),
  });
  assertApiResponse(res);
  if (!res.ok) throw new Error(`Push failed (${res.status})`);
  await putTest({ ...t, syncState: "uploaded", everUploaded: true });
}

async function pullTests(): Promise<number> {
  let since: string | null | undefined;
  try {
    since = (await getReference(WATERMARK_KEY))?.version;
  } catch {
    // No watermark readable — fall through to a full pull.
  }

  const url = since ? `/api/sync/tests?since=${encodeURIComponent(since)}` : "/api/sync/tests";
  const res = await fetch(url, { headers: { Accept: "application/json" }, redirect: "manual" });
  assertApiResponse(res);
  if (!res.ok) throw new Error(`Pull failed (${res.status})`);
  const { watermark, tests: remote } = (await res.json()) as PullResponse;

  let added = 0;
  for (const r of remote) {
    // A DIRTY local copy wins (it holds edits the server hasn't seen — they'll push next).
    // A CLEAN ("uploaded") copy is by definition one the server has seen, so the server's
    // current state replaces it — otherwise a device that pulled an in-progress draft would
    // keep it stale forever and never receive the completed version or its amendment history.
    const existing = await getTest(r.clientId);
    if (existing && existing.syncState !== "uploaded") continue;
    const now = new Date().toISOString();

    // Prefer the full payload (exact rehydration); fall back to the header for older tests.
    let local: LocalTest | null = null;
    if (r.payloadJson) {
      try {
        const parsed = JSON.parse(r.payloadJson) as Record<string, unknown>;
        if (parsed.legacy !== undefined && parsed.currentStep === undefined) {
          // MIGRATED legacy test: the payload is raw legacy columns, not a LocalTest. Adapt it into
          // a read-only LocalTest carrying the original (as-recorded) pass/fail verdicts.
          const adapted = adaptLegacyReadings(parsed);
          local = {
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
        } else {
          // New-format test: the payload IS a serialised LocalTest — rehydrate exactly.
          local = { ...(parsed as unknown as LocalTest), id: r.clientId, syncState: "uploaded", everUploaded: true };
        }
      } catch {
        local = null;
      }
    }
    local ??= {
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

    await putTest(local);
    added++;
  }

  // Advance the watermark only after every pulled test is stored: an interrupted pull re-fetches
  // the same window next time (safe — the loop upserts) instead of losing it.
  await putReference({ key: WATERMARK_KEY, version: watermark });
  return added;
}

/** Push every local-only test, then pull the Tester's tests down. Also flushes a pending
 * offline edit of the tester's calibration dates (kept dirty until the server accepts it). */
export async function syncAll(): Promise<SyncResult> {
  await flushCalibration();
  const locals = await allTests();
  let pushed = 0;
  let failed = 0;
  for (const t of locals) {
    if (t.syncState !== "local-only") continue;
    try {
      await pushTest(t);
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

  // A sync just succeeded, so the connection is real and the tester is almost certainly not
  // stuck in a paddock. That is the moment to pull down the report generator's lazy chunks, so
  // printing works on-farm later on a device that has never printed before. Deliberately not
  // awaited: it is ~2.4 MB and no one should wait on it to see their tests.
  void warmReportGenerator();

  return { pushed, failed, pulled };
}
