// Pulsator Test Results — a per-pulsator row table (rate + front/back ratio + phases b/d +
// chamber vacuum + limp) with the live spread checks (≤6 ppm rate, ≤5% ratio between pulsators,
// ≤5% limp), plus the ISO 14–15 ancillary / test-pulsation readings below. Phase limits per
// ISO 6690 Table D.5: b ≥ 30%, d ≥ 150 ms.
import { RowTable, type RowColumn } from "../ui/RowTable";
import { ReadingsStep } from "./ReadingsStep";
import { pulsationLimits, pulsatorSummary } from "../passfail/pulsatorStats";
import { pulsatorSections } from "../passfail/standards";
import { ruleFor } from "../passfail/standardsOverrides";
import type { MachineConfiguration } from "./types";
import type { MeasurementRow } from "../db/testStore";
import type { PassFailVerdict } from "../passfail/passFail";

// Built inside the component so the synced standard overrides are in effect.
function columnsFor(limpMax: number): RowColumn[] {
  return [
    { key: "rate", label: "Rate", unit: "ppm" },
    { key: "ratioFront", label: "Ratio front", unit: "%" },
    { key: "ratioBack", label: "Ratio back", unit: "%" },
    { key: "phaseB", label: "Phase b", unit: "%", rule: ruleFor("puls.row.phaseB", { kind: "atLeast", min: 30 }) },
    { key: "phaseDms", label: "Phase d", unit: "ms", rule: ruleFor("puls.row.phaseDms", { kind: "atLeast", min: 150 }) },
    { key: "maxVacuum", label: "Max chamber vac", unit: "kPa" },
    { key: "limp", label: "Limp", unit: "%", rule: { kind: "atMost", limit: limpMax } },
  ];
}

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
  readonly?: boolean;
  storedVerdicts?: Record<string, PassFailVerdict>;
}

export function PulsatorStep({ config, rows, onRows, readings, onSetReading, readonly, storedVerdicts }: Props) {
  const s = pulsatorSummary(rows, config.pulsatorModel);
  const limits = pulsationLimits();
  return (
    <>
      <div class="card">
        <div class="card__title">
          Pulsator test results{" "}
          <small class="card__hint">
            Add faulty pulsators, or “Enter all”. Rate spread ≤ {limits.rateSpreadMax} ppm, ratio spread ≤ {limits.ratioSpreadMax}%.
          </small>
        </div>
        <RowTable
          columns={columnsFor(limits.limpMax)}
          rows={rows}
          onChange={onRows}
          unitLabel="Pulsator"
          suggestedCount={config.pulsatorCount || undefined}
          readonly={readonly}
        />
        {rows.length > 0 && (
          <div class="puls-summary">
            <SpreadStat
              label="Rate"
              range={`${fmt(s.slowestRate)}–${fmt(s.fastestRate)} ppm`}
              spread={s.rateSpread}
              ok={s.rateSpreadOk}
              max={limits.rateSpreadMax}
              unit="ppm"
            />
            <SpreadStat
              label="Ratio"
              range={`${fmt(s.lowestRatio)}–${fmt(s.highestRatio)} %`}
              spread={s.ratioSpread}
              ok={s.ratioSpreadOk}
              max={limits.ratioSpreadMax}
              unit="%"
            />
            {s.rateBand && s.rateBandOk != null && (
              <div class="puls-stat">
                <span class="puls-stat__label">Model rate band</span>
                <span class={"pf pf--" + (s.rateBandOk ? "pass" : "fail")}>
                  {fmt(s.slowestRate)}–{fmt(s.fastestRate)} {s.rateBandOk ? "within" : "outside"} {s.rateBand.min}–{s.rateBand.max} ppm
                </span>
              </div>
            )}
            {s.ratioBand && s.ratioBandOk != null && (
              <div class="puls-stat">
                <span class="puls-stat__label">Model ratio band</span>
                <span class={"pf pf--" + (s.ratioBandOk ? "pass" : "fail")}>
                  {fmt(s.lowestRatio)}–{fmt(s.highestRatio)} {s.ratioBandOk ? "within" : "outside"} {s.ratioBand.min}–{s.ratioBand.max}%
                </span>
              </div>
            )}
            {s.worstLimp != null && (
              <div class="puls-stat">
                <span class="puls-stat__label">Limp</span>
                <span class={"pf pf--" + (s.limpOk ? "pass" : "fail")}>
                  worst {s.worstLimp}% {s.limpOk ? "≤" : ">"} {limits.limpMax}%
                </span>
              </div>
            )}
          </div>
        )}
      </div>
      <ReadingsStep
        title="Pulsator & ancillary readings"
        hint="ISO 14–15 air consumption + airline stability."
        sections={pulsatorSections(config, readings)}
        readings={readings}
        onSetReading={onSetReading}
        readonly={readonly}
        storedVerdicts={storedVerdicts}
      />
    </>
  );
}
