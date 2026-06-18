// Sync client — the only thing that talks to the server. Pushes local-only tests up
// (POST /api/sync/tests, upsert by ClientId) and pulls the Tester's tests down. Auth is the
// tester's cookie (same-origin fetch sends it automatically).
import { allTests, getTest, putTest, type LocalTest } from "../db/testStore";
import { defaultMachineConfiguration, type MachineConfiguration } from "../wizard/types";
import { adaptLegacyReadings } from "../report/legacyAdapter";

interface TestSummaryDto {
  clientId: string;
  farmName: string;
  createdAt: string;
  markedCompleteAt: string | null;
  config: MachineConfiguration | null;
  /** Full offline capture payload (the serialised LocalTest) for exact rehydration. */
  payloadJson: string | null;
}

export interface SyncResult {
  pushed: number;
  pulled: number;
}

async function pushTest(t: LocalTest): Promise<void> {
  const res = await fetch("/api/sync/tests", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      clientId: t.id,
      farmName: t.farmName,
      notes: t.notes ?? null,
      markedCompleteAt: t.markedCompleteAt ?? null,
      createdAt: t.createdAt,
      config: t.config,
      // The full rich capture round-trips as JSON so a re-download rehydrates exactly.
      payloadJson: JSON.stringify(t),
    }),
  });
  if (!res.ok) throw new Error(`Push failed (${res.status})`);
  await putTest({ ...t, syncState: "uploaded", everUploaded: true });
}

async function pullTests(): Promise<number> {
  const res = await fetch("/api/sync/tests", { headers: { Accept: "application/json" } });
  if (!res.ok) throw new Error(`Pull failed (${res.status})`);
  const remote = (await res.json()) as TestSummaryDto[];

  let added = 0;
  for (const r of remote) {
    if (await getTest(r.clientId)) continue; // local copy wins (authoritative for in-progress)
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
  return added;
}

/** Push every local-only test, then pull the Tester's tests down. */
export async function syncAll(): Promise<SyncResult> {
  const locals = await allTests();
  let pushed = 0;
  for (const t of locals) {
    if (t.syncState === "local-only") {
      await pushTest(t);
      pushed++;
    }
  }
  const pulled = await pullTests();
  return { pushed, pulled };
}
