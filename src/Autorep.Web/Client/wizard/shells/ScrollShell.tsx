// Layout B — single scroll. The whole test is one page of collapsible blocks, so a Tester who
// works from the paper order can move up and down freely instead of stepping through panels. Its
// own chrome is a sticky test bar with a chip strip for jumping and a bar at the foot showing
// what's still outstanding; the app header and centred column above it are the standard ones.
import { useState } from "preact/hooks";
import { renderStep } from "../WizardSteps";
import {
  STEP_DESCRIPTIONS,
  STEP_SHORT_LABELS,
  faultSummary,
  faultsInStep,
  overallProgress,
  stepProgress,
} from "../wizardProgress";
import type { WizardStep } from "../types";
import type { ShellProps } from "./types";

const blockId = (step: WizardStep) => `wz-block-${step}`;

/** What a jump has to clear: the app header and this layout's own bar are both pinned to the top of
 * the viewport, so a block scrolled to y=0 would land underneath them. Measured rather than a fixed
 * figure — both heights move with the viewport width and with how the chip strip wraps. */
function stickyOffset(): number {
  const height = (selector: string) =>
    document.querySelector(selector)?.getBoundingClientRect().height ?? 0;
  return height(".app-header") + height(".scrollw__bar") + 12;
}

export function ScrollShell({
  ctx,
  current,
  onGo,
  online,
  layoutMenu,
  banners,
  connectionLabel,
  exitHref,
  exitLabel,
}: ShellProps) {
  const { test, plan, completed } = ctx;
  // Explicit opens/closes only. Anything untouched falls back to "the step you're on is open",
  // so arriving mid-test lands you where you left off without collapsing your own work.
  const [toggled, setToggled] = useState<Partial<Record<WizardStep, boolean>>>({});
  const isOpen = (step: WizardStep) => toggled[step] ?? step === current;

  const progress = overallProgress(test);
  const faults = faultSummary(test);

  const jumpTo = (step: WizardStep) => {
    setToggled((t) => ({ ...t, [step]: true }));
    onGo(step);
    // Let the block expand before measuring where it landed.
    requestAnimationFrame(() => {
      const el = document.getElementById(blockId(step));
      if (el) {
        const top = el.getBoundingClientRect().top + window.scrollY - stickyOffset();
        window.scrollTo({ top: Math.max(0, top), behavior: "smooth" });
      }
    });
  };

  const nextUp = progress.firstIncomplete;

  return (
    <div class="wizard-shell wizard-shell--scroll">
      <div class="wizard-shell__bar-top scrollw__bar">
        <div class="scrollw__bar-inner">
          {/* No logo or nav here — the app header directly above already carries both. */}
          <div class="scrollw__ident">
            <div class="scrollw__farm">{test.farmName || "New machine test"}</div>
            <div class="scrollw__sub">
              {online ? "Machine test · one continuous record" : connectionLabel}
            </div>
          </div>
          <div class="scrollw__headline">
            <div class="scrollw__pct">{progress.pct}%</div>
            <div class="scrollw__sub">
              {progress.doneCount} of {progress.requiredCount} required steps
            </div>
          </div>
          {layoutMenu}
          <a class="scrollw__exit" href={exitHref}>{exitLabel}</a>
        </div>

        <div class="scrollw__track">
          <div class="scrollw__fill" style={`width:${progress.pct}%`} />
        </div>

        <div class="scrollw__chips">
          {plan.steps.map((s, i) => {
            const done = completed.has(s.step);
            return (
              <button
                key={s.step}
                type="button"
                class={
                  "scrollw__chip" +
                  (s.step === current ? " is-current" : "") +
                  (done ? " is-complete" : "")
                }
                onClick={() => jumpTo(s.step)}
              >
                <span class="scrollw__chip-num">{done ? "✓" : i + 1}</span>
                {STEP_SHORT_LABELS[s.step]}
              </button>
            );
          })}
        </div>
      </div>

      <main class="scrollw__main">
        {banners}

        {plan.steps.map((s, i) => {
          const done = completed.has(s.step);
          const frac = stepProgress(test, s.step);
          const stepFaults = faultsInStep(test, s.step);
          const open = isOpen(s.step);
          return (
            <section key={s.step} id={blockId(s.step)} class={"scrollw__block" + (open ? " is-open" : "")}>
              <button
                type="button"
                class="scrollw__block-head"
                aria-expanded={open}
                onClick={() => {
                  setToggled((t) => ({ ...t, [s.step]: !open }));
                  if (!open) onGo(s.step);
                }}
              >
                <span class={"scrollw__block-num" + (done ? " is-complete" : "")}>{done ? "✓" : i + 1}</span>
                <span class="scrollw__block-labels">
                  <span class="scrollw__block-title">
                    {s.title}
                    {s.isOptional && <span class="wizard__opt"> optional</span>}
                  </span>
                  <span class="scrollw__block-desc">{STEP_DESCRIPTIONS[s.step]}</span>
                </span>
                {stepFaults > 0 && (
                  <span class="scrollw__pill scrollw__pill--fault">
                    ⚑ {stepFaults} {stepFaults === 1 ? "fault" : "faults"}
                  </span>
                )}
                <span class={"scrollw__pill" + (done ? " scrollw__pill--done" : frac > 0 ? " scrollw__pill--part" : "")}>
                  {done
                    ? "Complete"
                    : frac > 0
                      ? `${Math.round(frac * 100)}% entered`
                      : s.isOptional
                        ? "Optional"
                        : "Not started"}
                </span>
                <span class="scrollw__chev" aria-hidden="true">▾</span>
              </button>

              {open && <div class="scrollw__block-body">{renderStep(ctx, s.step)}</div>}
            </section>
          );
        })}
      </main>

      <div class="wizard-shell__bar-foot scrollw__foot">
        <div class="scrollw__foot-inner">
          <div class="scrollw__counts">
            <span class={"scrollw__count" + (faults.critical > 0 ? " is-critical" : "")}>
              {faults.critical} critical
            </span>
            <span class={"scrollw__count" + (faults.major > 0 ? " is-major" : "")}>{faults.major} major</span>
            <span class="scrollw__count">{faults.minor} minor</span>
          </div>
          <div class="scrollw__nextup">
            {nextUp ? `Next up: ${nextUp.title}` : "All required steps done — ready to sign off"}
          </div>
          <button
            type="button"
            class="scrollw__go"
            onClick={() => jumpTo(nextUp ? nextUp.step : "ReviewSignOff")}
          >
            {nextUp ? "Go there ›" : "Review & sign off ›"}
          </button>
        </div>
      </div>
    </div>
  );
}
