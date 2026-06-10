// Individual Cluster Tests (optional, ISO 13) — a per-cluster row table: total air admission,
// leakage, air-vent admission. "Faulty clusters only" or "Enter all". Per-cell limits from
// ISO 6690 Table D.6: total ≤ 12 (vented liners ≤ 35 per the manual), leakage ≤ 2, air vent ≥ 4.
import { RowTable, type RowColumn } from "../ui/RowTable";
import type { MachineConfiguration } from "./types";
import type { MeasurementRow } from "../db/testStore";

function columnsFor(config: MachineConfiguration): RowColumn[] {
  return [
    {
      key: "totalAirAdmission",
      label: "Total air admission",
      unit: "L/min",
      rule: { kind: "atMost", limit: config.linerVented ? 35 : 12 },
    },
    { key: "leakage", label: "Leakage", unit: "L/min", rule: { kind: "atMost", limit: 2 } },
    { key: "airVent", label: "Air-vent admission", unit: "L/min", rule: { kind: "atLeast", min: 4 } },
  ];
}

interface Props {
  config: MachineConfiguration;
  rows: MeasurementRow[];
  onRows: (rows: MeasurementRow[]) => void;
}

export function ClusterStep({ config, rows, onRows }: Props) {
  return (
    <div class="card">
      <div class="card__title">
        Individual cluster tests{" "}
        <small class="card__hint">
          Optional · ISO 13. Total ≤ {config.linerVented ? "35 (vented)" : "12"}, leakage ≤ 2, air vent ≥ 4 L/min.
        </small>
      </div>
      <RowTable
        columns={columnsFor(config)}
        rows={rows}
        onChange={onRows}
        unitLabel="Cluster"
        suggestedCount={config.clusterCount || undefined}
      />
    </div>
  );
}
