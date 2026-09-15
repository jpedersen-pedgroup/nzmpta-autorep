// Numeric entry for readings and per-unit measurement rows. While the field is focused the browser
// owns the text — see numericEntry.ts for why writing the parsed value back breaks decimals — and
// the stored value takes over again on blur.
import { useState } from "preact/hooks";
import { readTypedNumber } from "./numericEntry";

interface Props {
  value: number | string | null | undefined;
  onValue: (value: number | null) => void;
  disabled?: boolean;
  class?: string;
  title?: string;
}

export function DecimalInput({ value, onValue, disabled, class: className, title }: Props) {
  const stored = value == null ? "" : String(value);
  // Non-null only while the tester is typing, and always a copy of the input's own text, so the
  // diff never rewrites the field under the caret.
  const [draft, setDraft] = useState<string | null>(null);

  // inputmode="decimal" gives tablets a keypad with a decimal point. It has no minus key on iPad,
  // which is fine now that every signed reading (1c, 5b, the gauge errors …) is calculated rather
  // than typed — see passfail/derived.ts.
  return (
    <input
      type="number"
      step="any"
      inputMode="decimal"
      class={className}
      title={title}
      value={draft ?? stored}
      disabled={disabled}
      onFocus={(e) => setDraft((e.currentTarget as HTMLInputElement).value)}
      onBlur={(e) => {
        // Preact only rewrites `value` when it differs from the DOM's, and a rejected fragment
        // ("-", ".", "41.") reads back as "" — so with nothing stored, the stray text would stay
        // on screen looking like a captured value. Discard it explicitly.
        const el = e.currentTarget as HTMLInputElement;
        if (el.value !== stored || el.validity.badInput) el.value = stored;
        setDraft(null);
      }}
      onInput={(e) => {
        if (disabled) return;
        const el = e.currentTarget as HTMLInputElement;
        setDraft(el.value);
        const next = readTypedNumber(el.value, el.validity.badInput, stored);
        if (next !== undefined) onValue(next);
      }}
    />
  );
}
