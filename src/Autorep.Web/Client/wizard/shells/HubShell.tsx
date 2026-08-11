// Layout C — task hub. An overview of the whole test (progress ring per step, fault counts, a
// resume card) that you tap into to work on one step at a time. Its own chrome — the hero on the
// overview, the sticky step bar in focus — sits inside the app's standard header and column.
import { useState } from "preact/hooks";
import { renderStep } from "../WizardSteps";
import {
  STEP_DESCRIPTIONS,
  faultSummary,
  faultsInStep,
  overallProgress,
  stepProgress,
  subsFor,
} from "../wizardProgress";
import type { PlantType, WizardStep } from "../types";
import type { ShellProps } from "./types";

const PLANT_LABELS: Record<PlantType, string> = {
  HerringboneLowline: "Herringbone (lowline)",
  HerringboneHighline: "Herringbone (highline)",
  Rotary: "Rotary",
  Other: "Other",
};

/** Circumference of the r=22 progress ring, for the stroke-dasharray split. */
const RING = 2 * Math.PI * 22;

export function HubShell({
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
  // The hub is the landing view; tapping a card drops into focus on that step. Kept in component
  // state, not on the test — which step you're looking at is navigation, not part of the record.
  const [focused, setFocused] = useState(false);

  const progress = overallProgress(test);
  const faults = faultSummary(test);
  const cfg = test.config;
  const plantSummary = `${PLANT_LABELS[cfg.plantType]} · ${cfg.clusterCount || "—"} clusters · ${
    cfg.pulsatorCount || "—"
  } pulsators`;

  const openStep = (step: WizardStep) => {
    onGo(step);
    setFocused(true);
    window.scrollTo({ top: 0 });
  };

  if (!focused) {
    const resume = progress.firstIncomplete;
    return (
      <div class="wizard-shell wizard-shell--hub">
        <div class="hubw__hero">
          {/* Test-level actions only — the app header directly above carries the brand and nav. */}
          <div class="hubw__hero-bar">
            <div class="hubw__spacer" />
            <span class={"hubw__status" + (online ? " is-online" : "")}>
              <span class="hubw__dot" aria-hidden="true" />
              {connectionLabel}
            </span>
            {layoutMenu}
            <a class="hubw__exit" href={exitHref}>{exitLabel}</a>
          </div>

          <div class="hubw__hero-body">
            <div class="hubw__ident">
              <div class="hubw__eyebrow">Machine test</div>
              <h1 class="hubw__farm">{test.farmName || "New machine test"}</h1>
              <div class="hubw__plant">{plantSummary}</div>
            </div>
            <div class="hubw__stats">
              <div class="hubw__stat">
                <div class="hubw__stat-value">
                  {progress.pct}
                  <span class="hubw__stat-unit">%</span>
                </div>
                <div class="hubw__stat-label">complete</div>
              </div>
              <div class="hubw__stat-divider" />
              <div class="hubw__stat">
                <div
                  class={
                    "hubw__stat-value" +
                    (faults.critical > 0 ? " is-critical" : faults.total > 0 ? " is-major" : " is-clear")
                  }
                >
                  {faults.total}
                </div>
                <div class="hubw__stat-label">faults logged</div>
              </div>
              <div class="hubw__stat-divider" />
              <div class="hubw__severities">
                <span class="hubw__sev hubw__sev--critical">{faults.critical} critical</span>
                <span class="hubw__sev hubw__sev--major">{faults.major} major</span>
                <span class="hubw__sev hubw__sev--minor">{faults.minor} minor</span>
              </div>
            </div>
          </div>
        </div>

        {/* Divs, not <main>s — see the note in ScrollShell: _Layout owns the document's one <main>,
            and nesting another applies the global main rule's column and padding on top. */}
        <div class="hubw__main">
          {banners}

          <button type="button" class="hubw__resume" onClick={() => openStep(resume ? resume.step : "ReviewSignOff")}>
            <span class="hubw__resume-icon" aria-hidden="true">▶</span>
            <span class="hubw__resume-labels">
              <span class="hubw__resume-title">{resume ? resume.title : "All required steps complete"}</span>
              <span class="hubw__resume-sub">
                {resume
                  ? // Match on the step key: overallProgress resolves its own plan, so this step is
                    // an equal-but-distinct object and identity lookup would never find it.
                    `Step ${plan.steps.findIndex((s) => s.step === resume.step) + 1} of ${plan.steps.length} · pick up where the test needs you next`
                  : "Head to Review & Sign-Off to complete the test"}
              </span>
            </span>
            <span class="hubw__resume-go">Continue testing ›</span>
          </button>

          <div class="hubw__cards">
            {plan.steps.map((s, i) => {
              const done = completed.has(s.step);
              const frac = Math.max(0, Math.min(1, stepProgress(test, s.step)));
              const stepFaults = faultsInStep(test, s.step);
              return (
                <button
                  key={s.step}
                  type="button"
                  class={"hubw__card" + (done ? " is-complete" : "")}
                  onClick={() => openStep(s.step)}
                >
                  <span class="hubw__ring">
                    <svg width="52" height="52" viewBox="0 0 52 52" aria-hidden="true">
                      <circle cx="26" cy="26" r="22" fill="none" stroke="var(--surface-alt)" stroke-width="5" />
                      <circle
                        cx="26"
                        cy="26"
                        r="22"
                        fill="none"
                        stroke={done ? "var(--success)" : frac > 0 ? "var(--brand)" : "var(--border)"}
                        stroke-width="5"
                        stroke-linecap="round"
                        stroke-dasharray={`${(frac * RING).toFixed(1)} ${RING.toFixed(1)}`}
                        transform="rotate(-90 26 26)"
                      />
                    </svg>
                    <span class={"hubw__ring-centre" + (done ? " is-complete" : "")}>{done ? "✓" : i + 1}</span>
                  </span>

                  <span class="hubw__card-body">
                    <span class="hubw__card-title">
                      {s.title}
                      {s.isOptional && <span class="wizard__opt"> optional</span>}
                    </span>
                    <span class="hubw__card-desc">{STEP_DESCRIPTIONS[s.step]}</span>
                    <span class="hubw__card-pills">
                      <span
                        class={
                          "hubw__pill" +
                          (done ? " hubw__pill--done" : frac > 0 ? " hubw__pill--part" : "")
                        }
                      >
                        {done
                          ? "Complete"
                          : frac > 0
                            ? `${Math.round(frac * 100)}% entered`
                            : s.isOptional
                              ? "Optional"
                              : "Not started"}
                      </span>
                      {stepFaults > 0 && (
                        <span class="hubw__pill hubw__pill--fault">
                          ⚑ {stepFaults} {stepFaults === 1 ? "fault" : "faults"}
                        </span>
                      )}
                    </span>
                  </span>
                  <span class="hubw__card-chev" aria-hidden="true">›</span>
                </button>
              );
            })}
          </div>
        </div>
      </div>
    );
  }

  // ---- focus view ----
  const idx = plan.steps.findIndex((s) => s.step === current);
  const stepDef = plan.steps[idx] ?? plan.steps[0];
  const prev = idx > 0 ? plan.steps[idx - 1] : null;
  const next = idx >= 0 && idx < plan.steps.length - 1 ? plan.steps[idx + 1] : null;
  // How many sections this step holds — shown as a count, not as pagination. Step bodies render
  // all their sections at once (readings stack, checklists carry their own tabs), so splitting
  // them here would mean teaching every step to render a slice of itself.
  const subCount = subsFor(test, current).length;

  return (
    <div class="wizard-shell wizard-shell--hub wizard-shell--focus">
      <div class="wizard-shell__bar-top hubw__focus-bar">
        <button type="button" class="hubw__back" onClick={() => setFocused(false)}>
          ‹ Overview
        </button>
        <div class="hubw__focus-ident">
          <div class="hubw__focus-title">{stepDef.title}</div>
          <div class="hubw__focus-crumb">
            Step {idx + 1} of {plan.steps.length}
            {subCount > 1 && ` · ${subCount} sections`}
          </div>
        </div>
        {/* Dots track the plan, not sections within this step — a compact rail you can jump from. */}
        <div class="hubw__dots">
          {plan.steps.map((s, i) => (
            <button
              key={s.step}
              type="button"
              title={s.title}
              aria-label={s.title}
              aria-current={s.step === current}
              class={
                "hubw__dot-btn" +
                (s.step === current ? " is-current" : "") +
                (completed.has(s.step) ? " is-complete" : "")
              }
              onClick={() => openStep(s.step)}
            />
          ))}
        </div>
        {layoutMenu}
      </div>

      <div class="hubw__focus-main">
        {banners}
        <div class="hubw__panel" key={current}>
          {renderStep(ctx, current)}
        </div>
      </div>

      <div class="wizard-shell__bar-foot hubw__focus-foot">
        <div class="hubw__focus-foot-inner">
          <button
            type="button"
            class="hubw__nav hubw__nav--prev"
            disabled={!prev}
            onClick={() => prev && openStep(prev.step)}
          >
            ‹ Previous
          </button>
          <div class="hubw__spacer" />
          <button
            type="button"
            class="hubw__nav hubw__nav--next"
            onClick={() => (next ? openStep(next.step) : setFocused(false))}
          >
            {next ? `Next: ${next.title.split(" (")[0].split(" — ")[0]} ›` : "Back to overview"}
          </button>
        </div>
      </div>
    </div>
  );
}
