// Test Record step: numerical readings (ISO groups 1–9) grouped into tabs, each with a live
// pass/fail verdict from the Pass/Fail Calculator against the standards.
import { evaluate } from "../passfail/passFail";
import { type ReadingDef, testRecordSections } from "../passfail/standards";
import type { MachineConfiguration } from "./types";
import { Tabs } from "../ui/Tabs";

interface Props {
  config: MachineConfiguration;
  readings: Record<string, number>;
  onSetReading: (key: string, value: number | null) => void;
}

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

export function TestRecordStep({ config, readings, onSetReading }: Props) {
  const tabs = testRecordSections(config).map((sec) => ({
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
        Test Record <small class="card__hint">Enter readings — pass/fail is live against the standard for this machine.</small>
      </div>
      <Tabs tabs={tabs} />
    </div>
  );
}
