// Individual Cluster Tests (optional, ISO 13) — a per-cluster row table: total air admission,
// leakage, air-vent admission. "Faulty clusters only" or "Enter all".
import { RowTable, type RowColumn } from "../ui/RowTable";
import type { MachineConfiguration } from "./types";
import type { MeasurementRow } from "../db/testStore";

const COLUMNS: RowColumn[] = [
  { key: "totalAirAdmission", label: "Total air admission", unit: "L/min" },
  { key: "leakage", label: "Leakage", unit: "L/min" },
  { key: "airVent", label: "Air-vent admission", unit: "L/min" },
];

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
        <small class="card__hint">Optional · ISO 13. Add faulty clusters, or “Enter all”.</small>
      </div>
      <RowTable
        columns={COLUMNS}
        rows={rows}
        onChange={onRows}
        unitLabel="Cluster"
        suggestedCount={config.clusterCount || undefined}
      />
    </div>
  );
}
