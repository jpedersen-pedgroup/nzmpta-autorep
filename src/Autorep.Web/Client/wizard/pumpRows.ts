// The vacuum- and releaser-pump detail rows on the Machine Configuration (legacy "Farm & Milking
// Machine" page 2: make / model / motor size, plus drives-milk-pump and regulator type for each
// vacuum pump). Every reader goes through these functions rather than the stored arrays:
// - Vacuum pumps follow the pump count, and pump N lines up with the pump-N readings (8a–8c), so
//   the rows are positional. Rows past the count stay stored — clearing the count box to retype it
//   must not throw away what was entered — but they are not shown, printed or compared.
// - Releaser pumps are listed only while "Releaser pump" is ticked, always with at least one row.
// Tests captured before these fields existed have no arrays at all; both read as blank rows.
import type { MachineConfiguration, ReleaserPumpDetail, VacuumPumpDetail } from "./types";

export function vacuumPumpRows(config: MachineConfiguration): VacuumPumpDetail[] {
  // Whole rows only, so the rows here and the 8a–8c reading sets in standards.ts always agree.
  return padded(config.vacuumPumps, Math.max(1, Math.floor(config.numberOfVacuumPumps || 0)));
}

export function releaserPumpRows(config: MachineConfiguration): ReleaserPumpDetail[] {
  if (!config.hasReleaserPump) return [];
  const stored = config.releaserPumps ?? [];
  return padded(stored, Math.max(1, stored.length));
}

/** The stored rows with row `index` patched — padded with blank rows as needed, never shortened. */
export function withRow<T extends object>(rows: readonly T[] | undefined, index: number, patch: Partial<T>): T[] {
  const out = [...(rows ?? [])];
  while (out.length <= index) out.push({} as T);
  out[index] = { ...out[index], ...patch };
  return out;
}

/** True when nothing has been entered on the row. */
export function isBlankPumpRow(row: VacuumPumpDetail | ReleaserPumpDetail): boolean {
  return Object.values(row).every((v) => v == null || v === false || (typeof v === "string" && v.trim() === ""));
}

function padded<T extends object>(rows: readonly T[] | undefined, length: number): T[] {
  const out: T[] = [];
  for (let i = 0; i < length; i++) out.push(rows?.[i] ?? ({} as T));
  return out;
}
