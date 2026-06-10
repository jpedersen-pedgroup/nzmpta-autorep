// Reusable numerical-readings step (Test Record, Additional Tests, Pulsator, Individual Cluster).
// Sections become tabs; each reading shows a live pass/fail verdict from the Pass/Fail Calculator.
import { evaluate } from "../passfail/passFail";
import type { ReadingDef, ReadingSection } from "../passfail/standards";
import { Tabs } from "../ui/Tabs";

function ReadingRow({
  reading,
  value,
  onSet,
}: {
  reading: ReadingDef;
  value: number | undefined;
  onSet: (key: string, value: number | null) => void;
}) {
  const verdict = evaluate(value ?? null, reading.rule);
  const label = verdict === "pass" ? "PASS" : verdict === "fail" ? "FAIL" : "—";

  return (
    <div class="reading">
      <div class="reading__label">
        {reading.label}
        {reading.hint && <span class="reading__hint">{reading.hint}</span>}
      </div>
      <input
        type="number"
        step="any"
        value={value ?? ""}
        onInput={(e) => {
          const raw = (e.currentTarget as HTMLInputElement).value;
          onSet(reading.key, raw === "" ? null : Number(raw));
        }}
      />
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
}

export function ReadingsStep({ title, hint, sections, readings, onSetReading }: Props) {
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
          <ReadingRow key={r.key} reading={r} value={readings[r.key]} onSet={onSetReading} />
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
