// Fault Summary & Recommendations step: faults carried from the visual checks + failed readings,
// grouped by the Fault Aggregator, each with an editable recommendation — then general comments
// for anything the farmer should know that no single fault covers.
import { aggregate } from "../faults/faultAggregator";
import { buildFaultInputs } from "../faults/buildFaults";
import type { LocalTest } from "../db/testStore";

interface Props {
  test: LocalTest;
  readonly?: boolean;
  onSetRecommendation: (key: string, value: string) => void;
  onSetNotes: (value: string) => void;
}

export function FaultSummaryStep({ test, readonly, onSetRecommendation, onSetNotes }: Props) {
  // Migrated tests carry their faults + recommendations as-recorded (not the new per-fault model),
  // so render those read-only rather than recomputing.
  if (test.recordedRecommendations !== undefined) {
    return <RecordedFaultSummary test={test} />;
  }

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
                  disabled={readonly}
                  onInput={(e) => !readonly && f.key && onSetRecommendation(f.key, (e.currentTarget as HTMLInputElement).value)}
                />
              </div>
            ))}
          </div>
        ))
      )}

      {/* The legacy Fault Summary was free text, and testers used it for things no fault line
          carries: adjustments made on the day, a milk-quality concern, a reading that is out of
          spec but fine for this herd. It prints on the report under the fault table. */}
      <div class="fault-notes">
        <label class="fault-notes__label" for="fault-notes">
          General comments
          <span class="fault-notes__hint">
            Anything the farmer should know that isn't tied to a fault above — printed on the report.
          </span>
        </label>
        <textarea
          id="fault-notes"
          class="fault-notes__input"
          rows={4}
          value={test.notes ?? ""}
          disabled={readonly}
          placeholder="e.g. Regulation undershoot was excessive — VSD settings adjusted and re-checked at time of test."
          onInput={(e) => !readonly && onSetNotes((e.currentTarget as HTMLTextAreaElement).value)}
        />
      </div>
    </div>
  );
}

// Read-only fault summary for a migrated test: faults + recommendations exactly as recorded.
function RecordedFaultSummary({ test }: { test: LocalTest }) {
  const recs = test.recordedRecommendations ?? [];
  const faults = test.recordedVisualFaults ?? [];
  const comment = test.notes?.trim();
  const empty = recs.length === 0 && faults.length === 0 && !comment;

  return (
    <div class="card">
      <div class="card__title">
        Fault Summary &amp; Recommendations{" "}
        <small class="card__hint">As recorded at the time of testing.</small>
      </div>

      {empty && (
        <p class="td-muted" style="margin-top:var(--space-3)">
          No faults or recommendations were recorded for this test.
        </p>
      )}

      {faults.length > 0 && (
        <div class="fault-group">
          <div class="fault-group__head"><span class="fault-group__name">Recorded faults</span></div>
          {faults.map((f) => (
            <div key={f} class="fault">
              <div class="fault__desc"><span class="pf-dot pf-dot--major"></span>{f}</div>
            </div>
          ))}
        </div>
      )}

      {recs.map((r) => (
        <div key={r.label} class="fault-group">
          <div class="fault-group__head"><span class="fault-group__name">{r.label}</span></div>
          <p style="white-space:pre-wrap;margin:var(--space-2) 0 0">{r.text}</p>
        </div>
      ))}

      {comment && (
        <div class="fault-group">
          <div class="fault-group__head"><span class="fault-group__name">General comments</span></div>
          <p style="white-space:pre-wrap;margin:var(--space-2) 0 0">{comment}</p>
        </div>
      )}
    </div>
  );
}
