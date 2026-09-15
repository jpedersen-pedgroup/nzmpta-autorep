// Pulsation & Ancillary (ISO 14–15) — the readings first: air consumption (14), test pulsation
// with the machine-level fastest/slowest rate and highest/lowest ratio off the analyser (15), and
// airline stability. The faulty-pulsator table below is for the units that failed: rate + front/
// back ratio + phases b/d + chamber vacuum + limp, keyed by the unit (bail) number, with per-row
// checks (phase b ≥ 30%, phase d ≥ 150 ms — ISO 6690 Table D.5 — limp ≤ 5%, the model's bands).
// It appears once a pulsation reading fails, or on request.
import { useState } from "preact/hooks";
import { RowTable, type RowColumn } from "../ui/RowTable";
import { recordedRows } from "../ui/measurementRows";
import { ReadingsStep } from "./ReadingsStep";
import { pulsationLimits, pulsatorSummary } from "../passfail/pulsatorStats";
import { pulsatorSections } from "../passfail/standards";
import { ruleFor } from "../passfail/standardsOverrides";
import { evaluate, type PassFailVerdict } from "../passfail/passFail";
import type { MachineConfiguration } from "./types";
import type { MeasurementRow } from "../db/testStore";

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
  const sections = pulsatorSections(config, readings);
  // A just-added, still-blank row is not a result: keep it out of the figures.
  const recorded = recordedRows(rows);
  const s = pulsatorSummary(recorded, config.pulsatorModel);
  const limits = pulsationLimits();
  // The table is for the units that failed, so it earns its place once a pulsation reading is
  // over its limit (Josh: "show based on the 14 answers"). A single bad unit doesn't always move
  // the machine-level figures, so the tester can also open it by hand. Any row at all — even one
  // added and left blank — keeps it open: the shells remount this step on navigation, and a row
  // the tester can't see is one they can't fill in or remove.
  const anyFail = sections.some((sec) => sec.readings.some((r) => evaluate(readings[r.key], r.rule) === "fail"));
  const [revealed, setRevealed] = useState(false);
  const showTable = anyFail || rows.length > 0 || revealed;

  return (
    <>
      <ReadingsStep
        title="Pulsator & ancillary readings"
        hint="ISO 14–15 air consumption, test pulsation + airline stability."
        sections={sections}
        readings={readings}
        onSetReading={onSetReading}
        readonly={readonly}
        storedVerdicts={storedVerdicts}
      />
      <div class="card">
        <div class="card__title">
          Faulty pulsators{" "}
          <small class="card__hint">
            {showTable
              ? "Add each pulsator that failed, by unit number — its readings off the analyser."
              : "Only needed when a pulsation reading is over its limit."}
          </small>
        </div>
        {showTable ? (
          <>
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
            {recorded.length > 0 && (s.rateBand || s.ratioBand || s.worstLimp != null) && (
              <div class="puls-summary">
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
          </>
        ) : (
          <p class="td-muted" style="margin-top:var(--space-2)">
            No pulsation reading is over its limit, so there are no faulty pulsators to record.{" "}
            {!readonly && (
              <button type="button" class="btn btn--secondary btn--sm" onClick={() => setRevealed(true)}>
                Record a faulty pulsator anyway
              </button>
            )}
          </p>
        )}
      </div>
    </>
  );
}
