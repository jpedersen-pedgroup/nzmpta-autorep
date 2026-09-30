// "Choose sections…" beside Download report on the sign-off step: print the whole report, or just
// the sections wanted — the legacy app printed its seven reports one at a time, and a tester often
// wants only the page the farmer needs. Built on the same usePopover hook as the layout cog, so it
// floats clear of the sign-off card's fixed-height footer instead of squeezing it.
import { useEffect, useRef, useState } from "preact/hooks";
import { popoverInlineStyle, usePopover } from "../ui/popover";
import type { ReportPart, ReportPartOption } from "../report/testSummaryPdf";

interface Props {
  /** The sections this test has something in, in report order. */
  options: ReportPartOption[];
  disabled: boolean;
  /** Called with the ticked sections, or with none when every one is ticked — the full report. */
  onDownload: (only?: ReportPart[]) => void;
}

export function ReportSectionPicker({ options, disabled, onDownload }: Props) {
  const [open, setOpen] = useState(false);
  // What's been unticked rather than what's ticked, so the picker starts as the full report.
  const [unticked, setUnticked] = useState<ReadonlySet<ReportPart>>(() => new Set());
  const anchorRef = useRef<HTMLDivElement>(null);
  const popupRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const style = usePopover(open, anchorRef, popupRef, () => setOpen(false));

  useEffect(() => {
    if (open) popupRef.current?.querySelector<HTMLInputElement>("input")?.focus();
  }, [open]);

  const chosen = options.filter((o) => !unticked.has(o.part)).map((o) => o.part);
  const everything = chosen.length === options.length;

  const tick = (part: ReportPart, on: boolean) => {
    const next = new Set(unticked);
    if (on) next.delete(part);
    else next.add(part);
    setUnticked(next);
  };
  const download = () => {
    setOpen(false);
    onDownload(everything ? undefined : chosen);
  };

  return (
    <div class="report-picker" ref={anchorRef}>
      <button
        type="button"
        ref={triggerRef}
        class="btn btn--secondary"
        aria-haspopup="dialog"
        aria-expanded={open}
        disabled={disabled}
        onClick={() => setOpen(!open)}
      >
        Choose sections…
      </button>

      {open && (
        <div
          class="popover report-picker__popup"
          ref={popupRef}
          style={popoverInlineStyle(style)}
          role="dialog"
          aria-label="Choose the report sections to print"
          onKeyDown={(e) => {
            if (e.key !== "Escape") return;
            e.preventDefault();
            setOpen(false);
            triggerRef.current?.focus();
          }}
          // Tabbing out of the list closes it, as clicking away does.
          onFocusOut={(e) => {
            const next = e.relatedTarget as Node | null;
            if (next && !anchorRef.current?.contains(next)) setOpen(false);
          }}
        >
          <div class="report-picker__head">
            <span class="report-picker__heading">Sections to print</span>
            <button type="button" class="report-picker__quick" onClick={() => setUnticked(new Set())}>
              All
            </button>
            <button type="button" class="report-picker__quick" onClick={() => setUnticked(new Set(options.map((o) => o.part)))}>
              None
            </button>
          </div>
          {options.map((o) => (
            <label key={o.part} class="report-picker__option">
              <input
                type="checkbox"
                checked={!unticked.has(o.part)}
                onChange={(e) => tick(o.part, (e.currentTarget as HTMLInputElement).checked)}
              />
              <span>
                <span class="report-picker__name">{o.label}</span>
                {o.hint && <span class="report-picker__hint">{o.hint}</span>}
              </span>
            </label>
          ))}
          <div class="report-picker__foot">
            <p class="report-picker__note">The compliance disclaimer prints on page one, whichever sections you choose.</p>
            <button type="button" class="btn btn--full" disabled={chosen.length === 0} onClick={download}>
              {everything
                ? "Download full report (PDF)"
                : chosen.length === 0
                  ? "Tick at least one section"
                  : `Download ${chosen.length} section${chosen.length === 1 ? "" : "s"} (PDF)`}
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
