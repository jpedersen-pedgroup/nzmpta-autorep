// Sync client — the only thing that talks to the server. Pushes local-only tests up
// (POST /api/sync/tests, upsert by ClientId) and pulls the Tester's tests down. Auth is the
// tester's cookie (same-origin fetch sends it automatically).
import { allTests, getTest, putTest, type LocalTest } from "../db/testStore";
import { defaultMachineConfiguration, type MachineConfiguration } from "../wizard/types";

interface TestSummaryDto {
  clientId: string;
  farmName: string;
  createdAt: string;
  markedCompleteAt: string | null;
  config: MachineConfiguration | null;
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
    }),
  });
  if (!res.ok) throw new Error(`Push failed (${res.status})`);
  await putTest({ ...t, syncState: "uploaded" });
}

async function pullTests(): Promise<number> {
  const res = await fetch("/api/sync/tests", { headers: { Accept: "application/json" } });
  if (!res.ok) throw new Error(`Pull failed (${res.status})`);
  const remote = (await res.json()) as TestSummaryDto[];

  let added = 0;
  for (const r of remote) {
    if (await getTest(r.clientId)) continue; // local copy wins (authoritative for in-progress)
    const now = new Date().toISOString();
    await putTest({
      id: r.clientId,
      farmName: r.farmName,
      config: r.config ?? defaultMachineConfiguration(),
      currentStep: "Setup",
      visualFaults: {},
      attestations: [],
      readings: {},
      recommendations: {},
      createdAt: r.createdAt,
      updatedAt: now,
      markedCompleteAt: r.markedCompleteAt,
      syncState: "uploaded",
    });
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
