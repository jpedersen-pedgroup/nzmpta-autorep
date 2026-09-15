// Pulsation & Ancillary — the ISO 14–15 readings first (air consumption, test pulsation, airline
// stability), then a table for the pulsators that failed on the analyser: rate + front/back ratio
// + phases b/d + chamber vacuum + limp, keyed by the unit (bail) number the tester reads off the
// machine, with the live spread checks (≤6 ppm rate, ≤5% ratio between pulsators, ≤5% limp).
// Phase limits per ISO 6690 Table D.5: b ≥ 30%, d ≥ 150 ms.
import { RowTable, type RowColumn } from "../ui/RowTable";
import { recordedRows } from "../ui/measurementRows";
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

// The rows are the units that FAILED, not the whole machine, so their spread can prove the
// machine is over the limit but never that it is within it — a spread inside the limit gets no
// verdict. Capturing the machine-level fastest/slowest is a Phase 2 item (see the plan).
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
      {spread != null && ok === false && (
        <span class="pf pf--fail">
          spread {spread} {unit} &gt; {max}
        </span>
      )}
      {spread != null && ok !== false && (
        <span class="puls-stat__range">
          spread {spread} {unit} · limit {max}, across the units recorded
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
  // A just-added, still-blank row is not a result: keep it out of the spread figures.
  const recorded = recordedRows(rows);
  const s = pulsatorSummary(recorded, config.pulsatorModel);
  const limits = pulsationLimits();
  return (
    <>
      <ReadingsStep
        title="Pulsator & ancillary readings"
        hint="ISO 14–15 air consumption + airline stability."
        sections={pulsatorSections(config, readings)}
        readings={readings}
        onSetReading={onSetReading}
        readonly={readonly}
        storedVerdicts={storedVerdicts}
      />
      <div class="card">
        <div class="card__title">
          Faulty pulsators{" "}
          <small class="card__hint">
            Add each pulsator that failed, by unit number. Spread limits: rate ≤ {limits.rateSpreadMax} ppm, ratio ≤{" "}
            {limits.ratioSpreadMax}% — a spread over the limit among the units recorded is a fail.
          </small>
        </div>
        <RowTable
          columns={columnsFor(limits.limpMax)}
          rows={rows}
          onChange={onRows}
          unitLabel="Pulsator"
          unitColumn="Unit no."
          unitMax={config.clusterCount || undefined}
          unitMaxNoun="bails"
          readonly={readonly}
        />
        {recorded.length > 0 && (
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
    </>
  );
}
