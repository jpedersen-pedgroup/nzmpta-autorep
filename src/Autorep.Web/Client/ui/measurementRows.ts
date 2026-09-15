// Helpers over per-unit measurement rows (pulsators, clusters).
import type { MeasurementRow } from "../db/testStore";

/** A row the tester actually filled in — a unit number or at least one reading. An "＋ Add" that
 * was tapped and then abandoned leaves a wholly blank row; that is not recorded data and must not
 * complete a step, print on the report, or appear in the amendment history. */
export function isRecordedRow(r: MeasurementRow): boolean {
  return r.unit.trim() !== "" || Object.values(r.values).some((v) => (v ?? "").trim() !== "");
}

export function recordedRows(rows: MeasurementRow[] | undefined): MeasurementRow[] {
  return (rows ?? []).filter(isRecordedRow);
}
