// Review & Sign-Off — a read-only summary of the test (farm, plant, fault counts, step
// completion), a Tester attestation, and Mark-as-Complete which stamps the completion time and
// syncs to the server. Once complete it shows the synced state + a (coming-soon) report action.
import { useRef, useState } from "preact/hooks";
import { aggregate } from "../faults/faultAggregator";
import { buildFaultInputs } from "../faults/buildFaults";
import type { LocalTest } from "../db/testStore";
import type { PlantType, ResolvedWizardStep, WizardStep } from "./types";

function fmtSize(bytes: number): string {
  return bytes >= 1024 * 1024 ? `${(bytes / 1024 / 1024).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`;
}

const PLANT_LABELS: Record<PlantType, string> = {
  HerringboneLowline: "Herringbone (lowline)",
  HerringboneHighline: "Herringbone (highline)",
  Rotary: "Rotary",
  Other: "Other",
};

function fmtDate(iso?: string | null): string {
  if (!iso) return "—";
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleString();
}

interface Props {
  test: LocalTest;
  steps: ResolvedWizardStep[];
  completed: Set<WizardStep>;
  syncing: boolean;
  onMarkComplete: () => void;
  onResync: () => void;
  onDownloadReport: () => void;
  /** Attach the pulsation analyser's PDF (validated PDF-only by the caller too). */
  onAttachPdf: (file: File) => void;
  onRemovePdf: () => void;
}

export function ReviewSignOffStep({
  test,
  steps,
  completed,
  syncing,
  onMarkComplete,
  onResync,
  onDownloadReport,
  onAttachPdf,
  onRemovePdf,
}: Props) {
  const [attested, setAttested] = useState(false);
  const [dragOver, setDragOver] = useState(false);
  const fileInput = useRef<HTMLInputElement>(null);
  const summary = aggregate(buildFaultInputs(test));
  const isComplete = Boolean(test.markedCompleteAt);
  const reviewable = steps.filter((s) => s.step !== "ReviewSignOff");

  const pickFile = (files: FileList | null | undefined) => {
    const file = files?.[0];
    if (file) onAttachPdf(file);
  };

  return (
    <div class="card">
      <div class="card__title">
        Review &amp; sign-off <small class="card__hint">Check the test over, then mark it complete to sync.</small>
      </div>

      <div class="signoff-grid">
        <div>
          <span class="signoff__label">Farm</span>
          <div>{test.farm?.name ?? test.farmName ?? "—"}</div>
        </div>
        <div>
          <span class="signoff__label">Plant</span>
          <div>
            {PLANT_LABELS[test.config.plantType]} · {test.config.clusterCount || "—"} clusters
          </div>
        </div>
        <div>
          <span class="signoff__label">Pulsators</span>
          <div>{test.config.pulsatorCount || "—"}</div>
        </div>
      </div>

      <div class="fault-counts" style="margin-top:var(--space-3)">
        <span class="badge badge--danger">{summary.critical} critical</span>
        <span class="badge badge--warning">{summary.major} major</span>
        <span class="badge">{summary.minor} minor</span>
      </div>

      <div class="signoff-steps">
        {reviewable.map((s) => {
          const done = completed.has(s.step);
          const cls = done ? "is-done" : s.isOptional ? "is-opt" : "is-todo";
          return (
            <div key={s.step} class="signoff-step">
              <span class={"signoff-step__dot " + cls} />
              {s.title}
              {s.isOptional ? " (optional)" : ""}
            </div>
          );
        })}
      </div>

      <div class="signoff-attach">
        <div class="signoff__label" style="margin-bottom:4px">Pulsation analyser report (PDF)</div>
        {test.pulsationPdf ? (
          <div class="attach-chip">
            <span class="attach-chip__icon">📄</span>
            <span class="attach-chip__name">{test.pulsationPdf.name}</span>
            <span class="attach-chip__size">{fmtSize(test.pulsationPdf.size)} · appended to the report</span>
            <button class="attach-chip__remove" title="Remove attachment" onClick={onRemovePdf}>×</button>
          </div>
        ) : (
          <div
            class={"dropzone" + (dragOver ? " is-over" : "")}
            onDragOver={(e) => {
              e.preventDefault();
              setDragOver(true);
            }}
            onDragLeave={() => setDragOver(false)}
            onDrop={(e) => {
              e.preventDefault();
              setDragOver(false);
              pickFile(e.dataTransfer?.files);
            }}
            onClick={() => fileInput.current?.click()}
          >
            Drop the pulsation PDF here, or click to browse — it's appended to the Test Summary report.
            <input
              ref={fileInput}
              type="file"
              accept="application/pdf,.pdf"
              style="display:none"
              onChange={(e) => {
                pickFile((e.currentTarget as HTMLInputElement).files);
                (e.currentTarget as HTMLInputElement).value = "";
              }}
            />
          </div>
        )}
      </div>

      {isComplete ? (
        <div class="signoff-complete">
          <p>
            ✓ Marked complete {fmtDate(test.markedCompleteAt)} · sync:{" "}
            <strong>{test.syncState === "uploaded" ? "synced" : test.syncState}</strong>
          </p>
          <div class="form-actions">
            <button class="btn" onClick={onDownloadReport}>Download report (PDF)</button>
            <button class="btn btn--secondary" disabled={syncing} onClick={onResync}>
              {syncing ? "Syncing…" : "Sync again"}
            </button>
          </div>
        </div>
      ) : (
        <div class="signoff-actions">
          <label class="form-check">
            <input type="checkbox" checked={attested} onChange={(e) => setAttested((e.currentTarget as HTMLInputElement).checked)} />
            <span>I confirm this test has been completed and the results are accurate.</span>
          </label>
          <button class="btn" disabled={!attested || syncing} onClick={onMarkComplete}>
            {syncing ? "Completing…" : "Mark as complete &amp; sync"}
          </button>
        </div>
      )}
    </div>
  );
}
