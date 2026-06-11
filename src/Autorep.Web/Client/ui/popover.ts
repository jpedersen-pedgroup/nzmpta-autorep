// Shared positioning + dismissal for the custom dropdown popups (Combobox, Select, DatePicker).
// Popups render with position:fixed so they escape the wizard panel's internal scroll
// (overflow-y:auto would clip an absolutely-positioned child). The hook anchors the popup under
// its control, flips it above when there's no room below, tracks scroll/resize (including the
// mobile keyboard via visualViewport), closes when the anchor scrolls out of view, and closes on
// outside pointer-down or Escape.
import { useLayoutEffect, useRef, useState } from "preact/hooks";
import type { RefObject } from "preact";

const GAP = 4; // px between the control and the popup
const VIEWPORT_PAD = 8;

export interface PopoverStyle {
  top: number;
  left: number;
  minWidth?: number;
}

export function usePopover(
  open: boolean,
  anchorRef: RefObject<HTMLElement>,
  popupRef: RefObject<HTMLElement>,
  onClose: () => void,
  opts?: { matchWidth?: boolean },
): PopoverStyle | null {
  const [style, setStyle] = useState<PopoverStyle | null>(null);
  // Once placed, keep the same side while it still fits — otherwise a combobox that
  // grows/shrinks as it filters near the bottom of the screen flips on every keystroke.
  const placedAbove = useRef(false);
  const matchWidth = opts?.matchWidth ?? false;

  useLayoutEffect(() => {
    if (!open) {
      setStyle(null);
      placedAbove.current = false;
      return;
    }
    const compute = () => {
      const anchor = anchorRef.current;
      const popup = popupRef.current;
      if (!anchor || !popup) return;
      const rect = anchor.getBoundingClientRect();
      // visualViewport tracks the on-screen keyboard; fall back to the layout viewport.
      const vv = window.visualViewport;
      const vw = vv?.width ?? window.innerWidth;
      const vh = vv?.height ?? window.innerHeight;
      // Anchor scrolled out of view → the popup would float detached (over the sticky
      // header / step rail). Dismiss instead of tracking it off-screen.
      if (rect.bottom < 0 || rect.top > vh || rect.right < 0 || rect.left > vw) {
        onClose();
        return;
      }
      const popW = popup.offsetWidth;
      const popH = popup.offsetHeight;
      const fitsBelow = rect.bottom + GAP + popH <= vh - VIEWPORT_PAD;
      const fitsAbove = rect.top - GAP - popH >= VIEWPORT_PAD;
      const above = placedAbove.current ? fitsAbove : !fitsBelow && fitsAbove;
      placedAbove.current = above;
      // Clamp so the popup is always reachable even when neither side truly fits.
      const top = Math.max(VIEWPORT_PAD, above ? rect.top - GAP - popH : rect.bottom + GAP);
      const left = Math.max(VIEWPORT_PAD, Math.min(rect.left, vw - popW - VIEWPORT_PAD));
      const minWidth = matchWidth ? rect.width : undefined;
      setStyle((prev) =>
        prev && prev.top === top && prev.left === left && prev.minWidth === minWidth ? prev : { top, left, minWidth },
      );
    };
    compute();

    const onPointerDown = (e: PointerEvent) => {
      const t = e.target as Node;
      if (anchorRef.current?.contains(t) || popupRef.current?.contains(t)) return;
      onClose();
    };
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    // Capture-phase scroll so the internal wizard-panel scroll repositions the popup too.
    document.addEventListener("scroll", compute, true);
    window.addEventListener("resize", compute);
    window.visualViewport?.addEventListener("resize", compute);
    window.visualViewport?.addEventListener("scroll", compute);
    document.addEventListener("pointerdown", onPointerDown);
    document.addEventListener("keydown", onKeyDown);
    // The popup resizes as a combobox filters its options — keep it anchored.
    const ro = popupRef.current ? new ResizeObserver(compute) : null;
    if (popupRef.current && ro) ro.observe(popupRef.current);
    return () => {
      document.removeEventListener("scroll", compute, true);
      window.removeEventListener("resize", compute);
      window.visualViewport?.removeEventListener("resize", compute);
      window.visualViewport?.removeEventListener("scroll", compute);
      document.removeEventListener("pointerdown", onPointerDown);
      document.removeEventListener("keydown", onKeyDown);
      ro?.disconnect();
    };
  }, [open, matchWidth]);

  return style;
}

export function popoverInlineStyle(style: PopoverStyle | null): string {
  if (!style) return "visibility:hidden;top:0;left:0";
  const width = style.minWidth != null ? `;min-width:${style.minWidth}px` : "";
  return `top:${style.top}px;left:${style.left}px${width}`;
}
