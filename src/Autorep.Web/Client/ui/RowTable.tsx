// Editable per-unit measurement table (pulsators, clusters). The Tester can add faulty rows only,
// or "Enter all" to seed one row per unit from the configured count. Cells are numeric; a column
// can carry a pass/fail rule — failing cells highlight red as the value is typed.
import type { MeasurementRow } from "../db/testStore";
import { evaluate, type PassFailRule } from "../passfail/passFail";

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
  /** Singular noun for a unit, e.g. "Pulsator" / "Cluster". */
  unitLabel: string;
  /** Count to seed when "Enter all" is used (e.g. pulsator / cluster count). */
  suggestedCount?: number;
}

export function RowTable({ columns, rows, onChange, unitLabel, suggestedCount }: Props) {
  const addRow = () =>
    onChange([...rows, { id: crypto.randomUUID(), unit: String(rows.length + 1), values: {} }]);

  const fillAll = () => {
    const n = suggestedCount ?? 0;
    onChange(
      Array.from({ length: n }, (_, i) => ({ id: crypto.randomUUID(), unit: String(i + 1), values: {} })),
    );
  };

  const setCell = (id: string, key: string, value: string) =>
    onChange(rows.map((r) => (r.id === id ? { ...r, values: { ...r.values, [key]: value } } : r)));
  const setUnit = (id: string, unit: string) =>
    onChange(rows.map((r) => (r.id === id ? { ...r, unit } : r)));
  const removeRow = (id: string) => onChange(rows.filter((r) => r.id !== id));

  return (
    <div>
      <div class="rowtable-actions">
        <button class="btn btn--secondary btn--sm" onClick={addRow}>
          ＋ Add {unitLabel.toLowerCase()}
        </button>
        {suggestedCount ? (
          <button class="btn btn--secondary btn--sm" onClick={fillAll}>
            Enter all ({suggestedCount})
          </button>
        ) : null}
        {rows.length > 0 ? (
          <button class="btn btn--secondary btn--sm" onClick={() => onChange([])}>
            Clear
          </button>
        ) : null}
      </div>
      {rows.length === 0 ? (
        <p class="td-muted" style="margin-top:var(--space-2)">
          No {unitLabel.toLowerCase()} rows yet — add faulty units only, or “Enter all”.
        </p>
      ) : (
        <div class="rowtable-wrap">
          <table class="rowtable">
            <thead>
              <tr>
                <th>{unitLabel}</th>
                {columns.map((c) => (
                  <th key={c.key}>
                    {c.label}
                    {c.unit ? ` (${c.unit})` : ""}
                  </th>
                ))}
                <th aria-label="remove"></th>
              </tr>
            </thead>
            <tbody>
              {rows.map((r) => (
                <tr key={r.id}>
                  <td>
                    <input
                      class="rowtable__unit"
                      value={r.unit}
                      onInput={(e) => setUnit(r.id, (e.currentTarget as HTMLInputElement).value)}
                    />
                  </td>
                  {columns.map((c) => {
                    const raw = r.values[c.key] ?? "";
                    const failed =
                      c.rule != null && raw.trim() !== "" && evaluate(Number(raw), c.rule) === "fail";
                    return (
                      <td key={c.key}>
                        <input
                          type="number"
                          class={failed ? "is-fail" : undefined}
                          value={raw}
                          onInput={(e) => setCell(r.id, c.key, (e.currentTarget as HTMLInputElement).value)}
                        />
                      </td>
                    );
                  })}
                  <td>
                    <button class="rowtable__del" title="Remove" onClick={() => removeRow(r.id)}>
                      ×
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
