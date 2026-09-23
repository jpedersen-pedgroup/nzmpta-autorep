// The vacuum-pump, regulator and releaser-pump detail rows on the Machine Configuration (legacy
// "Farm & Milking Machine" page 2: make / model / motor size, plus drives-milk-pump for each vacuum
// pump). Every reader goes through these functions rather than the stored arrays:
// - Vacuum pumps follow the pump count, and pump N lines up with the pump-N readings (8a–8c), so
//   the rows are positional. Rows past the count stay stored — clearing the count box to retype it
//   must not throw away what was entered — but they are not shown, printed or compared.
// - Releaser pumps are listed only while "Releaser pump" is ticked, always with at least one row.
// - Regulators are a list of type + quantity, always with at least one row. Legacy (and this app
//   until 23 Sep 2026) kept one regulator type per pump row; a test that never had the list reads
//   those instead, one line per type counted across the pumps.
// Tests captured before these fields existed have no arrays at all; both read as blank rows.
import type { MachineConfiguration, RegulatorDetail, ReleaserPumpDetail, VacuumPumpDetail } from "./types";

export function vacuumPumpRows(config: MachineConfiguration): VacuumPumpDetail[] {
  // Whole rows only, so the rows here and the 8a–8c reading sets in standards.ts always agree.
  return padded(config.vacuumPumps, Math.max(1, Math.floor(config.numberOfVacuumPumps || 0)));
}

export function releaserPumpRows(config: MachineConfiguration): ReleaserPumpDetail[] {
  if (!config.hasReleaserPump) return [];
  const stored = config.releaserPumps ?? [];
  return padded(stored, Math.max(1, stored.length));
}

export function regulatorRows(config: MachineConfiguration): RegulatorDetail[] {
  if (config.regulators?.length) return config.regulators;
  const counts = new Map<string, number>();
  for (const p of vacuumPumpRows(config)) {
    const type = p.regulatorType?.trim();
    if (type) counts.set(type, (counts.get(type) ?? 0) + 1);
  }
  const fromPumps = [...counts].map(([type, quantity]) => ({ type, quantity }));
  return fromPumps.length ? fromPumps : [{}];
}

/** True when nothing has been entered on the regulator row. */
export function isBlankRegulatorRow(row: RegulatorDetail): boolean {
  return !row.type?.trim() && row.quantity == null;
}

/** The stored rows with row `index` patched — padded with blank rows as needed, never shortened. */
export function withRow<T extends object>(rows: readonly T[] | undefined, index: number, patch: Partial<T>): T[] {
  const out = [...(rows ?? [])];
  while (out.length <= index) out.push({} as T);
  out[index] = { ...out[index], ...patch };
  return out;
}

/** True when nothing has been entered on the row. The retired regulatorType doesn't count: it is
 * shown with the regulators now (regulatorRows), so a row holding only that has nothing to print. */
export function isBlankPumpRow(row: VacuumPumpDetail | ReleaserPumpDetail): boolean {
  return Object.entries(row).every(
    ([k, v]) => k === "regulatorType" || v == null || v === false || (typeof v === "string" && v.trim() === ""),
  );
}

function padded<T extends object>(rows: readonly T[] | undefined, length: number): T[] {
  const out: T[] = [];
  for (let i = 0; i < length; i++) out.push(rows?.[i] ?? ({} as T));
  return out;
}
