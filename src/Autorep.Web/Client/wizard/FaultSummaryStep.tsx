// Fault Summary & Recommendations step: faults carried from the visual checks + failed readings,
// grouped by the Fault Aggregator, each with an editable recommendation.
import { aggregate } from "../faults/faultAggregator";
import { buildFaultInputs } from "../faults/buildFaults";
import type { LocalTest } from "../db/testStore";

interface Props {
  test: LocalTest;
  onSetRecommendation: (key: string, value: string) => void;
}

export function FaultSummaryStep({ test, onSetRecommendation }: Props) {
  const summary = aggregate(buildFaultInputs(test));

  return (
    <div class="card">
      <div class="card__title">
        Fault Summary &amp; Recommendations{" "}
        <small class="card__hint">Carried from the visual checks and failed readings — add a recommendation for each.</small>
      </div>

      <div class="fault-counts">
        <span class="badge badge--danger">{summary.critical} critical</span>
        <span class="badge badge--warning">{summary.major} major</span>
        <span class="badge">{summary.minor} minor</span>
      </div>

      {summary.total === 0 ? (
        <p class="td-muted" style="margin-top:var(--space-3)">No faults recorded yet — the machine has passed every check so far.</p>
      ) : (
        summary.groups.map((g) => (
          <div key={g.component} class="fault-group">
            <div class="fault-group__head">
              <span class="fault-group__name">{g.component}</span>
              <span class={"badge " + (g.severity === "Critical" ? "badge--danger" : g.severity === "Major" ? "badge--warning" : "")}>
                {g.severity}
              </span>
            </div>
            {g.faults.map((f) => (
              <div key={f.key ?? f.description} class="fault">
                <div class="fault__desc">
                  <span class={`pf-dot pf-dot--${f.severity.toLowerCase()}`}></span>
                  {f.description}
                </div>
                <input
                  type="text"
                  class="fault__rec"
                  placeholder="Recommendation…"
                  value={f.recommendation ?? ""}
                  onInput={(e) => f.key && onSetRecommendation(f.key, (e.currentTarget as HTMLInputElement).value)}
                />
              </div>
            ))}
          </div>
        ))
      )}
    </div>
  );
}
