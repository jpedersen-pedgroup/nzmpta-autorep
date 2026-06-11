// A combobox: a text input with a themed dropdown so the Tester can type to filter the options
// OR open the list and pick. Free text is still allowed (degrades gracefully for values not in
// the list). Previously backed by a native <datalist>, whose popup can't be styled — this renders
// its own listbox on the design system. Works offline; no dependencies.
import { useRef, useState } from "preact/hooks";
import { popoverInlineStyle, usePopover } from "./popover";

interface Props {
  value?: string | null;
  onChange: (value: string | null) => void;
  options: readonly string[];
  /** Unique id for the popup listbox (links aria-controls/activedescendant). */
  listId: string;
  placeholder?: string;
  class?: string;
}

export function Combobox({ value, onChange, options, listId, placeholder, class: className }: Props) {
  const [open, setOpen] = useState(false);
  // null = opened without typing → show the full list; string = filter as the user types.
  const [query, setQuery] = useState<string | null>(null);
  const [active, setActive] = useState(-1);
  const anchorRef = useRef<HTMLDivElement>(null);
  const popupRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const style = usePopover(open, anchorRef, popupRef, () => setOpen(false), { matchWidth: true });

  const q = (query ?? "").trim().toLowerCase();
  const filtered = q === "" ? options : options.filter((o) => o.toLowerCase().includes(q));

  const openList = (withQuery: string | null) => {
    setQuery(withQuery);
    setActive(-1);
    setOpen(true);
  };

  const pick = (o: string) => {
    onChange(o);
    setOpen(false);
    setQuery(null);
  };

  const moveActive = (delta: number) => {
    if (!open) {
      openList(null);
      return;
    }
    if (filtered.length === 0) return;
    const next = active < 0 ? (delta > 0 ? 0 : filtered.length - 1) : Math.max(0, Math.min(active + delta, filtered.length - 1));
    setActive(next);
    popupRef.current
      ?.querySelectorAll<HTMLElement>(".listbox__option")
      [next]?.scrollIntoView({ block: "nearest" });
  };

  return (
    <div class={"combobox" + (className ? ` ${className}` : "")} ref={anchorRef}>
      <input
        ref={inputRef}
        type="text"
        role="combobox"
        autocomplete="off"
        aria-expanded={open}
        aria-controls={listId}
        aria-autocomplete="list"
        aria-activedescendant={open && active >= 0 ? `${listId}-opt-${active}` : undefined}
        value={value ?? ""}
        placeholder={placeholder ?? "Type or select…"}
        onClick={() => {
          if (!open) openList(null);
        }}
        onInput={(e) => {
          const v = (e.currentTarget as HTMLInputElement).value;
          onChange(v.trim() === "" ? null : v);
          openList(v);
        }}
        onKeyDown={(e) => {
          if (e.key === "ArrowDown") {
            e.preventDefault();
            moveActive(1);
          } else if (e.key === "ArrowUp") {
            e.preventDefault();
            moveActive(-1);
          } else if (e.key === "Enter") {
            if (open && active >= 0 && filtered[active] != null) {
              e.preventDefault();
              pick(filtered[active]);
            } else {
              setOpen(false);
            }
          } else if (e.key === "Tab") {
            setOpen(false);
          }
        }}
      />
      <button
        type="button"
        class="field-btn"
        tabIndex={-1}
        aria-label="Show options"
        onPointerDown={(e) => e.preventDefault() /* keep focus on the input */}
        onClick={() => {
          if (open) {
            setOpen(false);
          } else {
            inputRef.current?.focus();
            openList(null);
          }
        }}
      >
        <svg width="12" height="12" viewBox="0 0 12 12" fill="none" stroke="currentColor" stroke-width="1.75" aria-hidden="true">
          <path d="M2.5 4.5 6 8l3.5-3.5" />
        </svg>
      </button>

      {open && (
        <div
          class="popover listbox"
          ref={popupRef}
          style={popoverInlineStyle(style)}
          id={listId}
          role="listbox"
        >
          {filtered.map((o, i) => {
            const selected = o === value;
            return (
              <button
                key={o}
                type="button"
                id={`${listId}-opt-${i}`}
                role="option"
                aria-selected={selected}
                class={"listbox__option" + (selected ? " is-selected" : "") + (i === active ? " is-active" : "")}
                tabIndex={-1}
                onPointerDown={(e) => e.preventDefault() /* don't blur the input before click */}
                onClick={() => pick(o)}
              >
                <span>{o}</span>
                {selected && <span class="listbox__check" aria-hidden="true">✓</span>}
              </button>
            );
          })}
          {filtered.length === 0 && (
            <div class="listbox__empty" role="option" aria-disabled="true" aria-selected={false}>
              No matches — your text will be used as entered.
            </div>
          )}
        </div>
      )}
    </div>
  );
}
