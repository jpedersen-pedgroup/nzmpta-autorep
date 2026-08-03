// Layout A — the step rail. Numbered steps down the left, one panel at a time, Back/Next at the
// foot. This is the original wizard chrome and stays the default; it keeps the standard app header
// rather than supplying its own.
import { renderStep } from "../WizardSteps";
import type { ShellProps } from "./types";

export function RailShell({
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
  const { test, plan, completed, serverTestId, colleagueName } = ctx;
  const idx = plan.steps.findIndex((s) => s.step === current);
  const prev = idx > 0 ? plan.steps[idx - 1].step : null;
  const next = idx >= 0 && idx < plan.steps.length - 1 ? plan.steps[idx + 1].step : null;

  return (
    <div>
      <div class="page-header page-header--wizard">
        <div class="page-header__heading">
          <h1>{test.farmName || "New machine test"}</h1>
          <p>
            <span class={online ? "badge badge--success" : "badge badge--warning"}>{connectionLabel}</span>
            {serverTestId && colleagueName && <> <span class="badge">Tested by {colleagueName}</span></>}
            {(test.version ?? 1) > 1 && <> <span class="badge">Version {test.version}</span></>}
          </p>
        </div>
        <div class="page-header__actions">
          {layoutMenu}
          {/* Nothing was changed on a server view, so it's "Back", not "Exit". */}
          <a
            class={serverTestId ? "btn btn--secondary btn--sm" : "btn btn--danger-soft btn--sm"}
            href={exitHref}
          >
            {exitLabel}
          </a>
        </div>
      </div>

      {banners}

      <div class="wizard">
        <nav class="wizard__rail">
          {plan.steps.map((s, i) => (
            <a
              key={s.step}
              class={
                "wizard__step" +
                (s.step === current ? " is-current" : "") +
                (completed.has(s.step) ? " is-complete" : "")
              }
              href="#"
              onClick={(e) => {
                e.preventDefault();
                onGo(s.step);
              }}
            >
              <span class="wizard__num">{i + 1}</span>
              <span class="wizard__step-labels">
                <span class="wizard__step-title">{s.title}</span>
                {s.isOptional && <span class="wizard__opt">optional</span>}
              </span>
            </a>
          ))}
        </nav>

        <div class="wizard__content">
          <div class="wizard__panel" key={current}>
            {renderStep(ctx, current)}
          </div>

          <div class="wizard__nav">
            <div>{prev && <button class="btn btn--secondary" onClick={() => onGo(prev)}>‹ Back</button>}</div>
            <div>{next && <button class="btn" onClick={() => onGo(next)}>Next ›</button>}</div>
          </div>
        </div>
      </div>
    </div>
  );
}
