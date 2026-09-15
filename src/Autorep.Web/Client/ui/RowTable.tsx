// Editable per-unit measurement table (pulsators, clusters). Testers record the units that FAILED —
// one row each, keyed by the number the tester reads off the machine (a pulsator by the bail it
// serves, a cluster by its number) — not every unit on the machine. Cells are numeric; a column can
// carry a pass/fail rule, and failing cells highlight red as the value is typed.
import { useRef } from "preact/hooks";
import type { MeasurementRow } from "../db/testStore";
import { evaluate, type PassFailRule } from "../passfail/passFail";
import { DecimalInput } from "./DecimalInput";

export interface RowColumn {
  key: string;
  label: string;
  unit?: string;
  /** Optional per-cell standard; cells failing it are highlighted. */
  rule?: PassFailRule;
}

interface Props {
  columns: RowColumn[];
  rows: MeasurementRow[];
  onChange: (rows: MeasurementRow[]) => void;
  /** Singular noun for a unit, e.g. "Pulsator" / "Cluster" — the add button and empty state. */
  unitLabel: string;
  /** Header for the unit-number column, e.g. "Unit no." */
  unitColumn: string;
  /** Highest unit number on this machine. A row numbered above it is flagged, not blocked. */
  unitMax?: number;
  /** What unitMax counts, for the warning text — "bails", "clusters". */
  unitMaxNoun?: string;
  /** Read-only (migrated/historical test): no editing, no add/remove controls. */
  readonly?: boolean;
}

export function RowTable({ columns, rows, onChange, unitLabel, unitColumn, unitMax, unitMaxNoun, readonly }: Props) {
  // The row just added takes the caret, so "add, type the unit number, tab into the readings" is
  // one motion. Cleared once focused so a later render can't steal it back.
  const focusRow = useRef<string | null>(null);
  const addRow = () => {
    const id = crypto.randomUUID();
    focusRow.current = id;
    onChange([...rows, { id, unit: "", values: {} }]);
  };

  const setCell = (id: string, key: string, value: number | null) =>
    onChange(
      rows.map((r) =>
        r.id === id ? { ...r, values: { ...r.values, [key]: value == null ? "" : String(value) } } : r,
      ),
    );
  const setUnit = (id: string, unit: string) =>
    onChange(rows.map((r) => (r.id === id ? { ...r, unit } : r)));
  const removeRow = (id: string) => onChange(rows.filter((r) => r.id !== id));

  // The same unit twice, a number this machine doesn't have, or readings with no unit against
  // them — worth a second look, but the tester may know better (bail numbering can outrun the
  // cluster count), so warn, don't block. Shown as text under the box: a tooltip never appears
  // on the iPad.
  const unitWarning = (r: MeasurementRow): string | undefined => {
    const unit = r.unit.trim();
    if (unit === "") {
      const hasValues = Object.values(r.values).some((v) => (v ?? "").trim() !== "");
      return hasValues ? "Enter the unit number" : undefined;
    }
    if (rows.some((o) => o.id !== r.id && o.unit.trim() === unit)) return `${unitLabel} ${unit} is entered twice`;
    const n = Number(unit);
    if (unitMax && Number.isFinite(n) && n > unitMax) {
      return `This machine has ${unitMax} ${unitMaxNoun ?? "units"} — check the number`;
    }
    return undefined;
  };

  return (
    <div>
      {!readonly && (
        <div class="rowtable-actions">
          <button class="btn btn--secondary btn--sm" onClick={addRow}>
            ＋ Add {unitLabel.toLowerCase()}
          </button>
          {rows.length > 0 ? (
            <button class="btn btn--secondary btn--sm" onClick={() => onChange([])}>
              Clear
            </button>
          ) : null}
        </div>
      )}
      {rows.length === 0 ? (
        <p class="td-muted" style="margin-top:var(--space-2)">
          {readonly
            ? `No ${unitLabel.toLowerCase()} rows were recorded.`
            : `No ${unitLabel.toLowerCase()} rows yet — add each ${unitLabel.toLowerCase()} that failed.`}
        </p>
      ) : (
        <div class="rowtable-wrap">
          <table class="rowtable">
            <thead>
              <tr>
                <th>{unitColumn}</th>
                {columns.map((c) => (
                  <th key={c.key}>
                    {c.label}
                    {c.unit ? ` (${c.unit})` : ""}
                  </th>
                ))}
                {!readonly && <th aria-label="remove"></th>}
              </tr>
            </thead>
            <tbody>
              {rows.map((r) => {
                const warning = readonly ? undefined : unitWarning(r);
                const warnId = `${r.id}-warn`;
                return (
                  <tr key={r.id}>
                    <td>
                      <input
                        class={"rowtable__unit" + (warning ? " is-warn" : "")}
                        aria-describedby={warning ? warnId : undefined}
                        value={r.unit}
                        disabled={readonly}
                        ref={(el) => {
                          if (el && focusRow.current === r.id) {
                            focusRow.current = null;
                            el.focus();
                          }
                        }}
                        onInput={(e) => {
                          if (readonly) return;
                          setUnit(r.id, (e.currentTarget as HTMLInputElement).value);
                        }}
                      />
                      {warning && (
                        <small id={warnId} class="rowtable__warn">
                          {warning}
                        </small>
                      )}
                    </td>
                    {columns.map((c) => {
                      const raw = r.values[c.key] ?? "";
                      const failed =
                        c.rule != null && raw.trim() !== "" && evaluate(Number(raw), c.rule) === "fail";
                      return (
                        <td key={c.key}>
                          <DecimalInput
                            class={failed ? "is-fail" : undefined}
                            value={raw === "" ? null : raw}
                            disabled={readonly}
                            onValue={(v) => setCell(r.id, c.key, v)}
                          />
                        </td>
                      );
                    })}
                    {!readonly && (
                      <td>
                        <button class="rowtable__del" title="Remove" onClick={() => removeRow(r.id)}>
                          ×
                        </button>
                      </td>
                    )}
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
