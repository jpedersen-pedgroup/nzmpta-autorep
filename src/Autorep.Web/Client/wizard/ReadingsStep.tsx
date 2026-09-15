// Reusable numerical-readings step (Test Record, Additional Tests, Pulsator, Individual Cluster).
// Sections become tabs; each reading shows a live pass/fail verdict from the Pass/Fail Calculator.
import { evaluate, type PassFailVerdict } from "../passfail/passFail";
import type { ReadingDef, ReadingSection } from "../passfail/standards";
import { Tabs } from "../ui/Tabs";
import { DecimalInput } from "../ui/DecimalInput";

function ReadingRow({
  reading,
  value,
  onSet,
  readonly,
  storedVerdict,
}: {
  reading: ReadingDef;
  value: number | undefined;
  onSet: (key: string, value: number | null) => void;
  readonly?: boolean;
  storedVerdict?: PassFailVerdict;
}) {
  // Read-only (migrated) tests show the as-recorded verdict; live tests recompute against the standard.
  const verdict = storedVerdict ?? evaluate(value ?? null, reading.rule);
  const label = verdict === "pass" ? "PASS" : verdict === "fail" ? "FAIL" : "—";

  // Hints (and the formula of a calculated reading) are recomputed against today's standards and
  // config — hidden in read-only (historical) mode, where the badge shows the as-recorded verdict.
  // "= 1a − 1b" for an expression; a table lookup ("manual p42 table, by cluster count") reads as is.
  const formula = reading.derived ? (/[−+×÷]/.test(reading.derived) ? `= ${reading.derived}` : reading.derived) : null;
  const hint = readonly ? null : [formula, reading.hint].filter(Boolean).join(" · ") || null;

  return (
    <div class="reading">
      <div class="reading__label">
        {reading.label}
        {reading.derived && <span class="reading__tag">calculated</span>}
        {hint && <span class="reading__hint">{hint}</span>}
      </div>
      {reading.derived ? (
        // Worked out from the readings above it (passfail/derived.ts) — nothing to type.
        <output class="reading__calc" aria-label={`${reading.label}, calculated`}>
          {value == null ? "—" : String(value)}
        </output>
      ) : (
        <DecimalInput value={value} disabled={readonly} onValue={(v) => onSet(reading.key, v)} />
      )}
      <span class="reading__unit">{reading.unit}</span>
      <span class={`pf pf--${verdict}`}>{label}</span>
    </div>
  );
}

interface Props {
  title: string;
  hint: string;
  sections: ReadingSection[];
  readings: Record<string, number>;
  onSetReading: (key: string, value: number | null) => void;
  readonly?: boolean;
  /** As-recorded verdicts per reading key (migrated tests) — overrides live evaluation. */
  storedVerdicts?: Record<string, PassFailVerdict>;
}

export function ReadingsStep({ title, hint, sections, readings, onSetReading, readonly, storedVerdicts }: Props) {
  if (sections.length === 0) {
    return (
      <div class="card">
        <div class="card__title">{title}</div>
        <p class="td-muted">No readings apply to this machine — you can move on.</p>
      </div>
    );
  }

  const tabs = sections.map((sec) => ({
    key: sec.key,
    label: sec.title,
    content: (
      <div>
        {sec.readings.map((r) => (
          <ReadingRow
            key={r.key}
            reading={r}
            value={readings[r.key]}
            onSet={onSetReading}
            readonly={readonly}
            storedVerdict={storedVerdicts?.[r.key]}
          />
        ))}
      </div>
    ),
  }));

  return (
    <div class="card">
      <div class="card__title">
        {title} <small class="card__hint">{hint}</small>
      </div>
      <Tabs tabs={tabs} />
    </div>
  );
}
