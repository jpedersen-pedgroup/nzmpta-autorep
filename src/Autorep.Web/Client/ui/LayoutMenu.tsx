// The cog in the wizard header: pick how the test workflow is laid out. Built on the same
// usePopover hook as Select/Combobox/DatePicker, so it positions, flips and dismisses identically
// and escapes the wizard panel's internal scroll.
import { useEffect, useRef, useState } from "preact/hooks";
import { popoverInlineStyle, usePopover } from "./popover";
import { LAYOUT_OPTIONS, type WizardLayout } from "../wizard/shells/types";

interface Props {
  value: WizardLayout;
  onChange: (layout: WizardLayout) => void;
}

export function LayoutMenu({ value, onChange }: Props) {
  const [open, setOpen] = useState(false);
  const anchorRef = useRef<HTMLDivElement>(null);
  const popupRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const style = usePopover(open, anchorRef, popupRef, () => setOpen(false));

  useEffect(() => {
    if (!open) return;
    const idx = Math.max(0, LAYOUT_OPTIONS.findIndex((o) => o.id === value));
    popupRef.current?.querySelectorAll<HTMLButtonElement>(".layout-menu__option")[idx]?.focus();
  }, [open]);

  const pick = (layout: WizardLayout) => {
    onChange(layout);
    setOpen(false);
    triggerRef.current?.focus();
  };

  const onPopupKeyDown = (e: KeyboardEvent) => {
    const buttons = Array.from(
      popupRef.current?.querySelectorAll<HTMLButtonElement>(".layout-menu__option") ?? [],
    );
    const idx = buttons.indexOf(document.activeElement as HTMLButtonElement);
    if (e.key === "ArrowDown" || e.key === "ArrowUp") {
      e.preventDefault();
      const next = e.key === "ArrowDown" ? Math.min(idx + 1, buttons.length - 1) : Math.max(idx - 1, 0);
      buttons[next]?.focus();
    } else if (e.key === "Escape" || e.key === "Tab") {
      setOpen(false);
      triggerRef.current?.focus();
      if (e.key === "Escape") e.preventDefault();
    }
  };

  return (
    <div class="layout-menu" ref={anchorRef}>
      <button
        type="button"
        ref={triggerRef}
        // Styled in currentColor so it reads correctly on the rail's light page header and on the
        // scroll/hub dark bars without the caller having to say which it is.
        class="layout-menu__trigger"
        aria-haspopup="menu"
        aria-expanded={open}
        aria-label="Change workflow layout"
        title="Change workflow layout"
        onClick={() => setOpen(!open)}
      >
        <i class="fa-solid fa-gear" aria-hidden="true"></i>
      </button>

      {open && (
        <div
          class="popover layout-menu__popup"
          ref={popupRef}
          style={popoverInlineStyle(style)}
          role="menu"
          onKeyDown={onPopupKeyDown}
        >
          <div class="layout-menu__heading">Workflow layout</div>
          {LAYOUT_OPTIONS.map((o) => {
            const selected = o.id === value;
            return (
              <button
                key={o.id}
                type="button"
                role="menuitemradio"
                aria-checked={selected}
                tabIndex={-1}
                class={"layout-menu__option" + (selected ? " is-selected" : "")}
                onClick={() => pick(o.id)}
              >
                <span class="layout-menu__icon" aria-hidden="true">
                  <i class={`fa-solid ${o.icon}`}></i>
                </span>
                <span class="layout-menu__labels">
                  <span class="layout-menu__name">{o.name}</span>
                  <span class="layout-menu__blurb">{o.blurb}</span>
                </span>
                {selected && <span class="layout-menu__check" aria-hidden="true">✓</span>}
              </button>
            );
          })}
          <div class="layout-menu__foot">Changes only how the test is laid out — your entries stay put.</div>
        </div>
      )}
    </div>
  );
}
