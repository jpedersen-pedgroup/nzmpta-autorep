// Themed single-select: a trigger button styled like a form input with a listbox popup on the
// design system (the native <select> dropdown can't be themed). Mirrors native semantics —
// options are value/label pairs and an empty-value option acts as the placeholder.
import { useEffect, useRef, useState } from "preact/hooks";
import { popoverInlineStyle, usePopover } from "./popover";

export interface SelectOption {
  value: string;
  label: string;
}

interface Props {
  value: string | null | undefined;
  onChange: (value: string) => void;
  options: readonly SelectOption[];
  id?: string;
  class?: string;
}

export function Select({ value, onChange, options, id, class: className }: Props) {
  const [open, setOpen] = useState(false);
  const anchorRef = useRef<HTMLDivElement>(null);
  const popupRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const style = usePopover(open, anchorRef, popupRef, () => setOpen(false), { matchWidth: true });

  // Unknown/null value renders blank rather than lying that the first option is selected.
  const current = options.find((o) => o.value === (value ?? "")) ?? { value: "", label: "" };
  const isPlaceholder = current.value === "";
  const listboxId = id ? `${id}-listbox` : undefined;

  // Focus the selected option when the popup opens so arrow keys continue from it.
  useEffect(() => {
    if (!open) return;
    const idx = Math.max(0, options.findIndex((o) => o.value === current.value));
    const buttons = popupRef.current?.querySelectorAll<HTMLButtonElement>(".listbox__option");
    buttons?.[idx]?.focus();
  }, [open]);

  const pick = (v: string) => {
    onChange(v);
    setOpen(false);
    triggerRef.current?.focus();
  };

  const onPopupKeyDown = (e: KeyboardEvent) => {
    const buttons = Array.from(popupRef.current?.querySelectorAll<HTMLButtonElement>(".listbox__option") ?? []);
    const idx = buttons.indexOf(document.activeElement as HTMLButtonElement);
    if (e.key === "ArrowDown" || e.key === "ArrowUp") {
      e.preventDefault();
      const next = e.key === "ArrowDown" ? Math.min(idx + 1, buttons.length - 1) : Math.max(idx - 1, 0);
      buttons[next]?.focus();
    } else if (e.key === "Home" || e.key === "End") {
      e.preventDefault();
      buttons[e.key === "Home" ? 0 : buttons.length - 1]?.focus();
    } else if (e.key === "Escape" || e.key === "Tab") {
      setOpen(false);
      triggerRef.current?.focus();
      if (e.key === "Escape") e.preventDefault();
    }
  };

  return (
    <div class={"ui-select" + (className ? ` ${className}` : "")} ref={anchorRef}>
      <button
        type="button"
        id={id}
        ref={triggerRef}
        class="select-trigger"
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-controls={open ? listboxId : undefined}
        onClick={() => setOpen(!open)}
        onKeyDown={(e) => {
          if ((e.key === "ArrowDown" || e.key === "ArrowUp") && !open) {
            e.preventDefault();
            setOpen(true);
          }
        }}
      >
        <span class={isPlaceholder ? "is-placeholder" : ""}>{current.label}</span>
        <svg width="12" height="12" viewBox="0 0 12 12" fill="none" stroke="currentColor" stroke-width="1.75" aria-hidden="true" class="select-trigger__chevron">
          <path d="M2.5 4.5 6 8l3.5-3.5" />
        </svg>
      </button>

      {open && (
        <div
          class="popover listbox"
          ref={popupRef}
          style={popoverInlineStyle(style)}
          id={listboxId}
          role="listbox"
          onKeyDown={onPopupKeyDown}
        >
          {options.map((o) => {
            const selected = o.value === current.value;
            return (
              <button
                key={o.value}
                type="button"
                role="option"
                aria-selected={selected}
                tabIndex={-1}
                class={"listbox__option" + (selected ? " is-selected" : "")}
                onClick={() => pick(o.value)}
              >
                <span>{o.label}</span>
                {selected && <span class="listbox__check" aria-hidden="true">✓</span>}
              </button>
            );
          })}
        </div>
      )}
    </div>
  );
}
