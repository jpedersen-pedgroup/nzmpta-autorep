// Review & Sign-Off — a read-only summary of the test (farm, plant, fault counts, step
// completion), a Tester attestation, and Mark-as-Complete which stamps the completion time and
// syncs to the server. Once complete it shows the synced state and the report download: the full
// report in one tap, or just the sections the tester picks.
import { useMemo, useRef, useState } from "preact/hooks";
import { aggregate } from "../faults/faultAggregator";
import { buildFaultInputs } from "../faults/buildFaults";
import type { LocalTest } from "../db/testStore";
import type { ResolvedWizardStep, WizardStep } from "./types";
import { PLANT_LABELS } from "./configLabels";
import { DatePicker } from "../ui/DatePicker";
import { formatDisplayDate } from "../calibration/status";
import { nzDate, proposedNextTestDate } from "./nextTestDate";
import { reportPartOptions, type ReportPart } from "../report/testSummaryPdf";
import { ReportSectionPicker } from "./ReportSectionPicker";
import type { StoredReport } from "./WizardSteps";
import { savedByAdministrator } from "../versioning/chain";

function fmtSize(bytes: number): string {
  return bytes >= 1024 * 1024 ? `${(bytes / 1024 / 1024).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`;
}

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
  /** True while the report PDF is being generated/merged — shows the busy overlay. */
  generating: boolean;
  /** Read-only view of a test held on the server (an admin's, or a colleague's). Hides the actions
   * that only make sense for a test on this device: syncing (it would run the VIEWER's own
   * push/pull from inside someone else's record) and attaching/removing the analyser PDF. */
  isServerView?: boolean;
  /** An administrator is editing this test as its next version: there's no sign-off or download
   * here — the edit bar at the top saves it, and the report comes from the saved version. */
  adminEditing?: boolean;
  /** A Super-Administrator's edit may attach, replace or remove the analyser PDF (O3)… */
  canEditAttachment?: boolean;
  /** …and correct the next test date, which a tester's own amendment can't move. */
  canEditNextTestDate?: boolean;
  /** Who performed the test, when that isn't the viewer. Shown on a server view in place of the
   * local sync state, which means nothing for a record held on the server. */
  colleagueName?: string | null;
  onMarkComplete: () => void;
  onResync: () => void;
  /** Download the report — the full report, or `only` those sections of it. */
  onDownloadReport: (only?: ReportPart[]) => void;
  /** Server view: the report as the tester signed it off, when the server holds one. */
  storedReport?: StoredReport | null;
  onDownloadStoredReport?: () => Promise<void>;
  /** Attach the pulsation analyser's PDF (validated PDF-only by the caller too). */
  onAttachPdf: (file: File) => void;
  onRemovePdf: () => void;
  /** The tester's choice of next test date; null returns to the twelve-month default. */
  onNextTestDateChange: (date: string | null) => void;
}

export function ReviewSignOffStep({
  test,
  steps,
  completed,
  syncing,
  generating,
  isServerView,
  adminEditing,
  canEditAttachment,
  canEditNextTestDate,
  colleagueName,
  onMarkComplete,
  onResync,
  onDownloadReport,
  storedReport,
  onDownloadStoredReport,
  onAttachPdf,
  onRemovePdf,
  onNextTestDateChange,
}: Props) {
  const [attested, setAttested] = useState(false);
  const [fetchingStored, setFetchingStored] = useState(false);
  const [dragOver, setDragOver] = useState(false);
  // The sections of the download in progress (none: the full report), for the busy message.
  const [printing, setPrinting] = useState<ReportPart[] | undefined>(undefined);
  const fileInput = useRef<HTMLInputElement>(null);
  const summary = aggregate(buildFaultInputs(test));
  const isComplete = Boolean(test.markedCompleteAt);
  // An administrator's version keeps the report made when it was saved, not one signed off on a device.
  const asSaved = Boolean(isServerView) && savedByAdministrator(test);
  const partOptions = useMemo(() => (isComplete ? reportPartOptions(test) : []), [test, isComplete]);
  const download = (only?: ReportPart[]) => {
    setPrinting(only);
    onDownloadReport(only);
  };
  const reviewable = steps.filter((s) => s.step !== "ReviewSignOff");
  // Before sign-off the picker shows what will be recorded (the default until the tester picks);
  // after it, the recorded date, read-only. An amendment can't change it at all: the date belongs
  // to the original test, and carrying out its recommendations doesn't restart the clock.
  const nowIso = new Date().toISOString();
  const isAmendment = Boolean(test.supersedesId);
  const nextTestDate = isComplete || isAmendment ? test.nextTestDate : proposedNextTestDate(test, nowIso);
  const nextTestInPast = !isComplete && nextTestDate != null && nextTestDate <= nzDate(nowIso);
  // A server view shows the attachment read-only, except in a Super-Administrator's edit.
  const attachmentEditable = !isServerView || Boolean(canEditAttachment);

  const pickFile = (files: FileList | null | undefined) => {
    const file = files?.[0];
    if (file) onAttachPdf(file);
  };

  return (
    <div class="card card--signoff">
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

      <div class="form-field signoff-next">
        <label class="signoff__label" for="next-test-date">Next test due</label>
        {canEditNextTestDate ? (
          <>
            <DatePicker id="next-test-date" value={test.nextTestDate ?? null} onChange={onNextTestDateChange} />
            <div class="form-field__hint">Correcting it here changes when the farm shows as due.</div>
          </>
        ) : isComplete || isServerView ? (
          <div>{nextTestDate ? formatDisplayDate(nextTestDate) : "—"}</div>
        ) : isAmendment ? (
          <>
            <div>{nextTestDate ? formatDisplayDate(nextTestDate) : "Twelve months from the original test"}</div>
            <div class="form-field__hint">Set by the original test. Amending a test doesn't change when it's next due.</div>
          </>
        ) : (
          <>
            <DatePicker id="next-test-date" value={nextTestDate} onChange={onNextTestDateChange} />
            <div class="form-field__hint">
              {nextTestInPast ? (
                <span class="signoff-next__warn">This date isn't in the future — check it before signing off.</span>
              ) : test.nextTestDate ? (
                "Printed on the report for the farmer."
              ) : (
                "Twelve months from today unless you change it. Printed on the report for the farmer."
              )}
            </div>
          </>
        )}
      </div>

      <div class="signoff-scroll">
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
            <span class="attach-chip__size">
              {fmtSize(test.pulsationPdf.size)} ·{" "}
              {test.pulsationPdf.base64 || isServerView
                ? "appended to the report"
                : "kept on the server to save space here — fetched when you print (needs signal)"}
            </span>
            {attachmentEditable && (
              <button class="attach-chip__remove" title="Remove attachment" onClick={onRemovePdf}>×</button>
            )}
          </div>
        ) : !attachmentEditable ? (
          <p class="td-muted" style="margin:0">None attached.</p>
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
      </div>

      <div class="signoff-footer">
        {adminEditing ? (
          <p class="td-muted" style="margin:0">
            Save this edit with the bar at the top of the page. The report is downloaded from the saved
            version, so it carries the change in its amendment history.
          </p>
        ) : isComplete ? (
          <div class="signoff-complete">
            <p>
              ✓ Completed {fmtDate(test.markedCompleteAt)}
              {isServerView ? (
                colleagueName ? <> · tested by <strong>{colleagueName}</strong></> : null
              ) : (
                <> · sync: <strong>{test.syncState === "uploaded" ? "synced" : test.syncState}</strong></>
              )}
            </p>
            <div class="form-actions">
              <button class="btn" disabled={generating} onClick={() => download()}>
                {generating ? "Generating…" : "Download report (PDF)"}
              </button>
              <ReportSectionPicker options={partOptions} disabled={generating} onDownload={download} />
              {isServerView && storedReport && onDownloadStoredReport && (
                <button
                  class="btn btn--secondary"
                  disabled={fetchingStored}
                  onClick={() => {
                    setFetchingStored(true);
                    void onDownloadStoredReport().finally(() => setFetchingStored(false));
                  }}
                >
                  {fetchingStored ? "Downloading…" : asSaved ? "Download the report as saved" : "Download the report as signed off"}
                </button>
              )}
              {!isServerView && (
                <button class="btn btn--secondary" disabled={syncing} onClick={onResync}>
                  {syncing ? "Syncing…" : "Sync again"}
                </button>
              )}
            </div>
            {isServerView && test.recordedRecommendations === undefined && (
              // Download report makes a new copy from the recorded data; this says what the other
              // button is, or why it isn't there. Not on a migrated test: those never had one.
              <p class="form-field__hint" style="margin:var(--space-2) 0 0" data-stored-report>
                {asSaved
                  ? storedReport
                    ? `"As saved" is the copy made in the admin portal when this version was saved (${fmtSize(storedReport.sizeBytes)}, received ${fmtDate(storedReport.storedAt)}).`
                    : "No copy of this version's report is held — it's kept when the version is saved in the admin portal."
                  : storedReport
                    ? `"As signed off" is the copy the tester's device made at sign-off (${fmtSize(storedReport.sizeBytes)}, received ${fmtDate(storedReport.storedAt)}).`
                    : "No copy of the report as signed off is held for this test — it was signed off before reports were kept, or the tester's device hasn't sent it yet."}
              </p>
            )}
          </div>
        ) : (
          <div class="signoff-actions">
            <label class="form-check">
              <input type="checkbox" checked={attested} onChange={(e) => setAttested((e.currentTarget as HTMLInputElement).checked)} />
              <span>I confirm this test has been completed and the results are accurate.</span>
            </label>
            <button
              class={"btn" + (attested && !syncing ? " btn--success" : "")}
              disabled={!attested || syncing}
              onClick={onMarkComplete}
            >
              {syncing ? "Completing…" : "Mark as complete & sync"}
            </button>
          </div>
        )}
      </div>

      {generating && (
        <div class="busy-overlay" role="status">
          <span class="spinner" aria-hidden="true" />
          {test.pulsationPdf && (!printing || printing.includes("analyser")) ? "Merging PDFs…" : "Generating report…"}
        </div>
      )}
    </div>
  );
}
