// Themed date picker: a dd/mm/yyyy text input with a calendar popup styled on the design system
// (the native browser calendar can't be themed). Stores ISO yyyy-mm-dd strings — the same shape
// the previous <input type="date"> persisted. Typing is still supported: d/m/yy, dd/mm/yyyy,
// dots or dashes all parse; invalid text reverts on blur.
import { useEffect, useRef, useState } from "preact/hooks";
import { popoverInlineStyle, usePopover } from "./popover";

interface Props {
  value?: string | null;
  onChange: (value: string | null) => void;
  id?: string;
  placeholder?: string;
}

const MONTHS = [
  "January", "February", "March", "April", "May", "June",
  "July", "August", "September", "October", "November", "December",
];
const DOW = ["Mo", "Tu", "We", "Th", "Fr", "Sa", "Su"];

function pad2(n: number): string {
  return String(n).padStart(2, "0");
}

function toIso(y: number, m: number, d: number): string {
  return `${y}-${pad2(m + 1)}-${pad2(d)}`;
}

function fromIso(iso: string | null | undefined): { y: number; m: number; d: number } | null {
  if (!iso) return null;
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(iso);
  if (!m) return null;
  return { y: Number(m[1]), m: Number(m[2]) - 1, d: Number(m[3]) };
}

function formatDisplay(iso: string | null | undefined): string {
  const p = fromIso(iso);
  return p ? `${pad2(p.d)}/${pad2(p.m + 1)}/${p.y}` : "";
}

/** Lenient dd/mm/yyyy parse (also d/m/yy, dots, dashes). Returns ISO or null. */
export function parseDisplayDate(text: string): string | null {
  const m = /^\s*(\d{1,2})[/.-](\d{1,2})[/.-](\d{2}(?:\d{2})?)\s*$/.exec(text);
  if (!m) return null;
  const d = Number(m[1]);
  const mo = Number(m[2]) - 1;
  let y = Number(m[3]);
  if (m[3].length === 2) y += 2000;
  if (mo < 0 || mo > 11) return null;
  if (d < 1 || d > new Date(y, mo + 1, 0).getDate()) return null;
  return toIso(y, mo, d);
}

interface DayCell {
  y: number;
  m: number;
  d: number;
  outside: boolean;
}

/** Whole weeks (Mon-first) covering the given month, with leading/trailing outside days. */
function monthGrid(y: number, m: number): DayCell[] {
  const lead = (new Date(y, m, 1).getDay() + 6) % 7;
  const daysInMonth = new Date(y, m + 1, 0).getDate();
  const cells: DayCell[] = [];
  for (let i = 0; i < lead; i++) {
    const dt = new Date(y, m, 1 - lead + i);
    cells.push({ y: dt.getFullYear(), m: dt.getMonth(), d: dt.getDate(), outside: true });
  }
  for (let d = 1; d <= daysInMonth; d++) cells.push({ y, m, d, outside: false });
  while (cells.length % 7 !== 0) {
    const last = cells[cells.length - 1];
    const dt = new Date(last.y, last.m, last.d + 1);
    cells.push({ y: dt.getFullYear(), m: dt.getMonth(), d: dt.getDate(), outside: true });
  }
  return cells;
}

export function DatePicker({ value, onChange, id, placeholder }: Props) {
  const [open, setOpen] = useState(false);
  const [text, setText] = useState(formatDisplay(value));
  const today = new Date();
  const initial = fromIso(value) ?? { y: today.getFullYear(), m: today.getMonth(), d: today.getDate() };
  const [view, setView] = useState({ y: initial.y, m: initial.m });
  const anchorRef = useRef<HTMLDivElement>(null);
  const popupRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const style = usePopover(open, anchorRef, popupRef, () => setOpen(false));

  // Reflect external value changes (sync pull, Clear elsewhere) into the input text.
  useEffect(() => {
    setText(formatDisplay(value));
  }, [value]);

  const openAt = (iso: string | null | undefined) => {
    const p = fromIso(iso) ?? { y: today.getFullYear(), m: today.getMonth(), d: 1 };
    setView({ y: p.y, m: p.m });
    setOpen(true);
  };

  const pick = (y: number, m: number, d: number) => {
    const iso = toIso(y, m, d);
    onChange(iso);
    setText(formatDisplay(iso));
    setOpen(false);
    inputRef.current?.focus(); // a keyboard pick would otherwise drop focus to <body>
  };

  const commitText = (raw: string) => {
    if (raw.trim() === "") {
      if (value != null) onChange(null);
      setText("");
      return;
    }
    const iso = parseDisplayDate(raw);
    if (iso) {
      if (iso !== value) onChange(iso);
      setText(formatDisplay(iso));
    } else {
      setText(formatDisplay(value)); // revert
    }
  };

  const shiftMonth = (delta: number) =>
    setView((v) => {
      const dt = new Date(v.y, v.m + delta, 1);
      return { y: dt.getFullYear(), m: dt.getMonth() };
    });

  const selected = fromIso(value);
  const isToday = (c: DayCell) =>
    c.y === today.getFullYear() && c.m === today.getMonth() && c.d === today.getDate();
  const isSelected = (c: DayCell) =>
    selected != null && c.y === selected.y && c.m === selected.m && c.d === selected.d;

  return (
    <div
      class="datepicker"
      ref={anchorRef}
      onFocusOut={(e: FocusEvent) => {
        // Tabbing (or clicking) into another control closes the calendar; focus moving within
        // the input/popup keeps it open. relatedTarget is null on some touch blurs — the
        // popover hook's outside-pointerdown handles those.
        const next = e.relatedTarget as Node | null;
        if (next && !anchorRef.current?.contains(next)) setOpen(false);
      }}
    >
      <input
        ref={inputRef}
        type="text"
        id={id}
        inputMode="numeric"
        autocomplete="off"
        placeholder={placeholder ?? "dd/mm/yyyy"}
        value={text}
        aria-haspopup="dialog"
        aria-expanded={open}
        onClick={() => {
          if (!open) openAt(value);
        }}
        onInput={(e) => setText((e.currentTarget as HTMLInputElement).value)}
        onBlur={(e) => commitText((e.currentTarget as HTMLInputElement).value)}
        onKeyDown={(e) => {
          if (e.key === "Enter") {
            commitText((e.currentTarget as HTMLInputElement).value);
            setOpen(false);
          } else if (e.key === "ArrowDown" && !open) {
            e.preventDefault();
            openAt(value);
          } else if (e.key === "Tab") {
            setOpen(false);
          }
        }}
      />
      <button
        type="button"
        class="field-btn"
        tabIndex={-1}
        aria-label="Open calendar"
        onPointerDown={(e) => e.preventDefault() /* keep focus on the input */}
        onClick={() => (open ? setOpen(false) : openAt(value))}
      >
        <svg width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5" aria-hidden="true">
          <rect x="1.75" y="2.75" width="12.5" height="11.5" rx="2" />
          <path d="M1.75 6.25h12.5M5 1.25v3M11 1.25v3" />
        </svg>
      </button>

      {open && (
        <div
          class="popover datepicker-pop"
          ref={popupRef}
          style={popoverInlineStyle(style)}
          role="dialog"
          aria-label="Choose date"
          onPointerDown={(e) => e.preventDefault() /* keep focus on the input while tapping around */}
          onKeyDown={(e) => {
            if (e.key === "Escape") inputRef.current?.focus(); // the popover hook closes it
          }}
        >
          <div class="cal__head">
            <button type="button" class="cal__nav" aria-label="Previous year" onClick={() => shiftMonth(-12)}>«</button>
            <button type="button" class="cal__nav" aria-label="Previous month" onClick={() => shiftMonth(-1)}>‹</button>
            <span class="cal__label" aria-live="polite">{MONTHS[view.m]} {view.y}</span>
            <button type="button" class="cal__nav" aria-label="Next month" onClick={() => shiftMonth(1)}>›</button>
            <button type="button" class="cal__nav" aria-label="Next year" onClick={() => shiftMonth(12)}>»</button>
          </div>
          <div class="cal__grid">
            {DOW.map((d) => (
              <span key={d} class="cal__dow">{d}</span>
            ))}
            {monthGrid(view.y, view.m).map((c) => (
              <button
                key={`${c.y}-${c.m}-${c.d}`}
                type="button"
                class={
                  "cal__day" +
                  (c.outside ? " is-outside" : "") +
                  (isToday(c) ? " is-today" : "") +
                  (isSelected(c) ? " is-selected" : "")
                }
                aria-label={`${c.d} ${MONTHS[c.m]} ${c.y}`}
                aria-current={isSelected(c) ? "date" : undefined}
                onClick={() => pick(c.y, c.m, c.d)}
              >
                {c.d}
              </button>
            ))}
          </div>
          <div class="cal__foot">
            <button
              type="button"
              class="cal__action cal__action--muted"
              onClick={() => {
                onChange(null);
                setText("");
                setOpen(false);
                inputRef.current?.focus();
              }}
            >
              Clear
            </button>
            <button
              type="button"
              class="cal__action"
              onClick={() => pick(today.getFullYear(), today.getMonth(), today.getDate())}
            >
              Today
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
