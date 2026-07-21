// Syncs the tester's own equipment calibration expiry dates (a TESTER profile fact — the
// instruments travel with the tester, not with a farm/test) between the server profile
// (/api/profile/calibration) and the on-device reference cache, so the wizard can show and
// edit them offline. Same dirty-wins rule as tests: an offline edit stays cached as dirty
// and is pushed on the next app load / sync; the server copy only replaces a CLEAN cache.
import { getReference, putReference } from "../db/testStore";
import type { CalibrationDates } from "../calibration/status";

interface CachedCalibration {
  values: CalibrationDates;
  /** True when the device holds an edit the server hasn't accepted yet. */
  dirty?: boolean;
}

const REF_KEY = "testerCalibration";

async function readCache(): Promise<CachedCalibration | null> {
  try {
    const entry = await getReference(REF_KEY);
    const rows = entry?.rows as CachedCalibration | undefined;
    return rows && typeof rows === "object" && "values" in rows ? rows : null;
  } catch {
    return null;
  }
}

async function writeCache(cached: CachedCalibration): Promise<void> {
  await putReference({ key: REF_KEY, rows: cached });
}

function normalize(dto: unknown): CalibrationDates {
  const d = (dto ?? {}) as Record<string, unknown>;
  const date = (v: unknown) => (typeof v === "string" && v.length > 0 ? v : null);
  return {
    airFlowMeters: date(d.airFlowMeters),
    pulsatorTesters: date(d.pulsatorTesters),
    vacuumGauges: date(d.vacuumGauges),
  };
}

/** PUT the full set to the tester's profile. False on any failure (offline, auth). */
async function push(values: CalibrationDates): Promise<boolean> {
  try {
    const res = await fetch("/api/profile/calibration", {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        airFlowMeters: values.airFlowMeters ?? null,
        pulsatorTesters: values.pulsatorTesters ?? null,
        vacuumGauges: values.vacuumGauges ?? null,
      }),
    });
    return res.ok;
  } catch {
    return false;
  }
}

/** App-load sync (tester pages only): flush a dirty offline edit, then pull the profile fresh.
 * If the flush can't reach the server, the dirty cache stays in effect — never clobbered. */
export async function initCalibration(): Promise<void> {
  try {
    const cached = await readCache();
    if (cached?.dirty) {
      if (!(await push(cached.values))) return;
      await writeCache({ values: cached.values, dirty: false });
    }
    const res = await fetch("/api/profile/calibration", { headers: { Accept: "application/json" } });
    if (!res.ok) return;
    await writeCache({ values: normalize(await res.json()), dirty: false });
  } catch {
    // Offline — the cached profile stays in effect.
  }
}

/** The last-synced (or locally edited) calibration dates; empty when never synced. */
export async function getCachedCalibration(): Promise<CalibrationDates> {
  return (await readCache())?.values ?? {};
}

/** Save an edit: cache it immediately (offline-safe), then try to push. */
export async function saveCalibration(values: CalibrationDates): Promise<"synced" | "offline"> {
  await writeCache({ values, dirty: true });
  if (await push(values)) {
    await writeCache({ values, dirty: false });
    return "synced";
  }
  return "offline";
}

/** Re-push a dirty cached edit if one is pending (called from the main sync loop). */
export async function flushCalibration(): Promise<void> {
  const cached = await readCache();
  if (!cached?.dirty) return;
  if (await push(cached.values)) await writeCache({ values: cached.values, dirty: false });
}
