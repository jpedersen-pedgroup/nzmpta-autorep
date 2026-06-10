// Pulsator Test Results — a per-pulsator row table (rate + front/back ratio + chamber vacuum +
// limp) with the live spread checks (≤6 ppm rate, ≤5% ratio), plus the ISO 14–15 ancillary /
// test-pulsation readings below.
import { RowTable, type RowColumn } from "../ui/RowTable";
import { ReadingsStep } from "./ReadingsStep";
import { RATE_SPREAD_MAX, RATIO_SPREAD_MAX, pulsatorSummary } from "../passfail/pulsatorStats";
import { pulsatorSections } from "../passfail/standards";
import type { MachineConfiguration } from "./types";
import type { MeasurementRow } from "../db/testStore";

const COLUMNS: RowColumn[] = [
  { key: "rate", label: "Rate", unit: "ppm" },
  { key: "ratioFront", label: "Ratio front", unit: "%" },
  { key: "ratioBack", label: "Ratio back", unit: "%" },
  { key: "maxVacuum", label: "Max chamber vac", unit: "kPa" },
  { key: "limp", label: "Limp", unit: "%" },
];

const fmt = (n: number | null): string => (n == null ? "—" : String(n));

function SpreadStat({
  label,
  range,
  spread,
  ok,
  max,
  unit,
}: {
  label: string;
  range: string;
  spread: number | null;
  ok: boolean | null;
  max: number;
  unit: string;
}) {
  return (
    <div class="puls-stat">
      <span class="puls-stat__label">{label}</span>
      <span class="puls-stat__range">{range}</span>
      {spread != null && (
        <span class={"pf pf--" + (ok ? "pass" : "fail")}>
          spread {spread} {unit} {ok ? "≤" : ">"} {max}
        </span>
      )}
    </div>
  );
}

interface Props {
  config: MachineConfiguration;
  rows: MeasurementRow[];
  onRows: (rows: MeasurementRow[]) => void;
  readings: Record<string, number>;
  onSetReading: (key: string, value: number | null) => void;
}

export function PulsatorStep({ config, rows, onRows, readings, onSetReading }: Props) {
  const s = pulsatorSummary(rows);
  return (
    <>
      <div class="card">
        <div class="card__title">
          Pulsator test results{" "}
          <small class="card__hint">
            Add faulty pulsators, or “Enter all”. Rate spread ≤ {RATE_SPREAD_MAX} ppm, ratio spread ≤ {RATIO_SPREAD_MAX}%.
          </small>
        </div>
        <RowTable
          columns={COLUMNS}
          rows={rows}
          onChange={onRows}
          unitLabel="Pulsator"
          suggestedCount={config.pulsatorCount || undefined}
        />
        {rows.length > 0 && (
          <div class="puls-summary">
            <SpreadStat
              label="Rate"
              range={`${fmt(s.slowestRate)}–${fmt(s.fastestRate)} ppm`}
              spread={s.rateSpread}
              ok={s.rateSpreadOk}
              max={RATE_SPREAD_MAX}
              unit="ppm"
            />
            <SpreadStat
              label="Ratio"
              range={`${fmt(s.lowestRatio)}–${fmt(s.highestRatio)} %`}
              spread={s.ratioSpread}
              ok={s.ratioSpreadOk}
              max={RATIO_SPREAD_MAX}
              unit="%"
            />
          </div>
        )}
      </div>
      <ReadingsStep
        title="Pulsator & ancillary readings"
        hint="ISO 14–15 air consumption + airline stability."
        sections={pulsatorSections(config)}
        readings={readings}
        onSetReading={onSetReading}
      />
    </>
  );
}
