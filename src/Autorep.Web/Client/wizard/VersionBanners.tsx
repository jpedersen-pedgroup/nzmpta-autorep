// Notices about a test's versions, shown in the banners slot above every step in every layout: who
// made this version when it wasn't the tester, what an automatic merge combined, and an edit made
// elsewhere while this one is still unfinished. In the admin portal, also the controls for editing a
// completed test as its next version (O2; PRD stories 49–50).
import type { ComponentChildren } from "preact";
import { useState } from "preact/hooks";
import type { LocalTest } from "../db/testStore";
import { authorOf, madeByOther, rivalsOf } from "../versioning/chain";
import type { EditScope } from "../versioning/adminEdit";
import type { Deletion } from "../sync/removals";

function when(iso?: string | null): string {
  if (!iso) return "—";
  const d = new Date(iso);
  return Number.isNaN(d.getTime())
    ? iso
    : d.toLocaleString("en-NZ", { day: "numeric", month: "short", year: "numeric", hour: "numeric", minute: "2-digit" });
}

const plural = (n: number, word: string) => `${n} ${word}${n === 1 ? "" : "s"}`;

/** Provenance of this version, for anyone looking at it: tester, colleague or administrator. On a
 * tester's device, also a test NZMPTA has deleted that is still here because it holds unsent edits. */
export function VersionNotices({ test, others, deletion }: {
  test: LocalTest;
  others: readonly LocalTest[];
  deletion?: Deletion | null;
}) {
  const own = madeByOther(test);
  const rivals = test.markedCompleteAt ? [] : rivalsOf(test, others);
  const merge = own?.merge;
  return (
    <>
      {deletion && (
        <div class="alert alert--danger version-note" data-version-note="deleted">
          🗑 <strong>NZMPTA deleted this test</strong>
          {deletion.at ? ` on ${when(deletion.at)}` : ""}
          {deletion.reason ? <>: “{deletion.reason}”</> : "."} Your changes on this device are still sent —
          they're kept with the deleted test — and then it's removed from this device.
        </div>
      )}
      {own && merge && (
        <div class="alert alert--info version-note" data-version-note="merged">
          🔀 <strong>Combined automatically</strong> on {when(own.amendedAt)}. Version {merge.headVersion}
          {merge.headBy ? ` (${merge.headBy})` : ""} and version {merge.fromVersion}
          {merge.fromBy ? ` (${merge.fromBy})` : ""} were both made from the same earlier version, so they
          were combined into this one.{" "}
          {merge.overlaps.length === 0
            ? "They changed different things, so everything from both is here."
            : `Where both changed the same thing, the later of the two to reach the server was kept — ${plural(
                merge.overlaps.length,
                "field",
              )}: ${merge.overlaps.map((o) => o.label).join(", ")}.`}{" "}
          Both versions stay on record, and the report's amendment history lists the details.
        </div>
      )}
      {own && !merge && (
        <div class="alert alert--info version-note" data-version-note="admin">
          ✏️ <strong>Version {own.version} was made by {authorOf(own)}</strong> on {when(own.amendedAt)}
          {own.reason ? <>: “{own.reason}”</> : "."}
        </div>
      )}
      {rivals.length > 0 && (
        <div class="alert alert--warning version-note" data-version-note="rival">
          ⚠️ <strong>This test was changed while you were working on this version</strong> —{" "}
          {rivals
            .map((r) => {
              const by = madeByOther(r);
              return `version ${r.version ?? 1}${by ? ` by ${authorOf(by)}` : ""}, ${when(r.markedCompleteAt)}`;
            })
            .join("; ")}
          . When you sign off, your changes are combined with theirs automatically: where you both
          changed the same thing, yours is kept. Both versions stay on record.
        </div>
      )}
    </>
  );
}

/** What the admin viewer is told about the version on screen (from /api/tests/{id}). */
export interface AdminView {
  version: number;
  editScope?: EditScope | null;
  editBlocked?: string | null;
  latestId?: string | null;
  latestVersion?: number | null;
  draft?: { version: number; startedAt: string } | null;
  testerName?: string | null;
  completedAt?: string | null;
  /** Soft-deleted: who, when and why. */
  deletion?: { at?: string | null; by?: string | null; reason?: string | null } | null;
  /** A Super-Administrator may delete it (when it isn't) or restore it (when it is). */
  canDelete?: boolean;
  canRestore?: boolean;
}

/** Why a save didn't happen, as the edit bar words it. */
export type SaveProblem =
  | { kind: "reason" }
  | { kind: "unchanged" }
  | { kind: "stale"; latestId?: string; latestVersion?: number }
  | { kind: "blocked"; reason: string }
  | { kind: "fields"; labels: string[] }
  | { kind: "signedOut" }
  | { kind: "network" }
  | { kind: "message"; text: string };

interface AdminEditBarProps {
  view: AdminView;
  /** The scope being edited in, or null when not editing. */
  editing: EditScope | null;
  reason: string;
  onReason(value: string): void;
  saving: boolean;
  problem: SaveProblem | null;
  /** Set right after a save brought the editor here: the version number saved. */
  savedVersion: string | null;
  generating: boolean;
  onStart(): void;
  onSave(): void;
  onCancel(): void;
  onDownload(): void;
  /** Soft-delete the test with a reason; resolves with a message when it didn't happen. */
  onDelete(reason: string): Promise<string | null>;
  onRestore(): Promise<string | null>;
}

const BLOCKED_TEXT: Record<string, string> = {
  "in-progress": "It hasn't been signed off yet, so it's still the tester's draft. It can be edited once it's complete.",
  migrated: "It was migrated from AutoRep Plus and reprints from the results recorded at the time, so it can't be edited here.",
  "no-record": "It was synced before full test records were kept, so there's nothing here to edit.",
};

function problemText(problem: SaveProblem): ComponentChildren {
  switch (problem.kind) {
    case "reason":
      return "Give a reason for the change — it's kept with the new version and printed in the amendment history.";
    case "unchanged":
      return "Nothing has been changed yet.";
    case "stale":
      return (
        <>
          Someone saved a newer version of this test{problem.latestVersion ? ` (version ${problem.latestVersion})` : ""} while
          you were editing, so this edit can't be saved over it.{" "}
          {problem.latestId && <a href={`/Admin/Tests/View/${problem.latestId}`}>Open the latest version</a>} and make your
          change there.
        </>
      );
    case "blocked":
      return BLOCKED_TEXT[problem.reason] ?? "This version can't be edited.";
    case "fields":
      return `Your role can't change ${problem.labels.join(", ")} — only the recommendations and general comments.`;
    case "signedOut":
      return "You've been signed out. Sign in again in another tab, then save — your changes are still here.";
    case "network":
      return "The server couldn't be reached. Your changes are still here — try again when you're back online.";
    case "message":
      return problem.text;
  }
}

/** The admin portal's edit controls for the version on screen. */
export function AdminEditBar(props: AdminEditBarProps) {
  const { view, editing, problem } = props;
  const next = view.version + 1;

  if (editing) {
    return (
      <div class="alert alert--info admin-edit" role="region" aria-label="Edit as a new version">
        <div class="admin-edit__head">
          ✏️ <strong>Editing as version {next}</strong> —{" "}
          {editing === "summary"
            ? "you can change the recommendations and the general comments on the Fault Summary step."
            : "change any field the test records."}{" "}
          Version {view.version} stays on record unchanged.
        </div>
        <label class="admin-edit__label" for="edit-reason">
          Reason for the change <span class="admin-edit__hint">(required — kept with the new version)</span>
        </label>
        <textarea
          id="edit-reason"
          class="admin-edit__reason"
          rows={2}
          maxLength={500}
          value={props.reason}
          onInput={(e) => props.onReason((e.currentTarget as HTMLTextAreaElement).value)}
        />
        {problem && (
          <p class="admin-edit__problem" role="alert">
            {problemText(problem)}
          </p>
        )}
        <div class="form-actions">
          <button class="btn btn--success" disabled={props.saving} onClick={props.onSave}>
            {props.saving ? "Saving…" : `Save as version ${next}`}
          </button>
          <button class="btn btn--secondary" disabled={props.saving} onClick={props.onCancel}>
            Cancel
          </button>
        </div>
      </div>
    );
  }

  return (
    <>
      {props.savedVersion && (
        <div class="alert alert--success admin-edit__saved" role="status">
          ✓ <strong>Saved as version {props.savedVersion}.</strong> The report now carries the change in its
          amendment history.{" "}
          <button class="btn btn--sm" disabled={props.generating} onClick={props.onDownload}>
            {props.generating ? "Generating…" : "Download report (PDF)"}
          </button>
        </div>
      )}
      {view.deletion ? (
        <DeletedNotice deletion={view.deletion} canRestore={Boolean(view.canRestore)} onRestore={props.onRestore} />
      ) : view.editScope ? (
        <div class="alert alert--info admin-edit__offer">
          📝 <strong>Version {view.version}</strong>
          {view.completedAt ? `, completed ${when(view.completedAt)}` : ""}
          {view.testerName ? ` by ${view.testerName}` : ""}.{" "}
          {view.editScope === "summary"
            ? "You can edit its recommendations and general comments; "
            : "You can change any field; "}
          the edit is saved as version {next}, and this version stays on record.{" "}
          <button class="btn btn--sm" onClick={props.onStart}>
            {view.editScope === "summary" ? "Edit summary & recommendations" : "Edit as a new version"}
          </button>
          {view.draft && (
            <p class="admin-edit__draft">
              ⚠️ {view.testerName ?? "The tester"} has an unfinished new version of this test (version{" "}
              {view.draft.version}, started {when(view.draft.startedAt)}). If you edit this one, the two are
              combined automatically when they sign theirs off.
            </p>
          )}
        </div>
      ) : view.editBlocked === "superseded" ? (
        <div class="alert alert--warning">
          📄 <strong>Version {view.version} has been replaced</strong>
          {view.latestVersion ? ` by version ${view.latestVersion}` : ""}. Edits are made from the current version.{" "}
          {view.latestId && (
            <a class="btn btn--sm btn--secondary" href={`/Admin/Tests/View/${view.latestId}`}>
              Open version {view.latestVersion ?? "latest"}
            </a>
          )}
        </div>
      ) : (
        <div class="alert alert--warning">
          📄 <strong>Read-only.</strong> {BLOCKED_TEXT[view.editBlocked ?? ""] ?? "This test can't be edited here."} Pass/fail
          is shown as recorded at the time of testing.
        </div>
      )}
      {view.canDelete && <DeleteControl onDelete={props.onDelete} />}
    </>
  );
}

/** A soft-deleted test, as the admin viewer shows it — with Restore for a Super-Administrator. */
function DeletedNotice({ deletion, canRestore, onRestore }: {
  deletion: NonNullable<AdminView["deletion"]>;
  canRestore: boolean;
  onRestore(): Promise<string | null>;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  return (
    <div class="alert alert--danger admin-edit__deleted" role="status">
      🗑 <strong>Deleted</strong>
      {deletion.by ? ` by ${deletion.by}` : ""}
      {deletion.at ? ` on ${when(deletion.at)}` : ""}
      {deletion.reason ? <>: “{deletion.reason}”</> : "."} Every version of this test is hidden from the lists
      and from Upcoming, and has been removed from the tester's device. It stays on record.{" "}
      {canRestore && (
        <button
          class="btn btn--sm btn--secondary"
          disabled={busy}
          onClick={async () => {
            setBusy(true);
            setError(await onRestore());
            setBusy(false);
          }}
        >
          {busy ? "Restoring…" : "Restore"}
        </button>
      )}
      {error && <p class="admin-edit__problem" role="alert">{error}</p>}
    </div>
  );
}

/** Soft-delete (PRD story 70): a Super-Administrator's, with a reason, confirmed in a dialog. */
function DeleteControl({ onDelete }: { onDelete(reason: string): Promise<string | null> }) {
  const [open, setOpen] = useState(false);
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const close = () => {
    if (busy) return;
    setOpen(false);
    setReason("");
    setError(null);
  };
  const submit = async () => {
    if (!reason.trim()) {
      setError("Give a reason — it's kept with the deleted test.");
      return;
    }
    setBusy(true);
    setError(await onDelete(reason.trim()));
    setBusy(false);
  };
  return (
    <div class="admin-edit__danger">
      <button class="btn btn--danger-soft btn--sm" onClick={() => setOpen(true)}>
        Delete test…
      </button>
      {open && (
        <div
          class="modal-overlay open"
          onClick={(e) => {
            if (e.target === e.currentTarget) close();
          }}
        >
          <div class="modal" role="dialog" aria-modal="true" aria-labelledby="delete-test-title">
            <div class="modal__title" id="delete-test-title">Delete this test?</div>
            <p>
              Every version of it is hidden from the lists and from Upcoming, and removed from the tester's
              device at its next sync. Changes the tester hasn't sent yet are never lost: they're kept with the
              deleted test. It stays on record, with your reason, and can be restored.
            </p>
            <label class="admin-edit__label" for="delete-reason">
              Reason <span class="admin-edit__hint">(required)</span>
            </label>
            <textarea
              id="delete-reason"
              class="admin-edit__reason"
              rows={3}
              maxLength={500}
              value={reason}
              onInput={(e) => setReason((e.currentTarget as HTMLTextAreaElement).value)}
            />
            {error && <p class="admin-edit__problem" role="alert">{error}</p>}
            <div class="form-actions">
              <button class="btn btn--danger" disabled={busy} onClick={() => void submit()}>
                {busy ? "Deleting…" : "Delete test"}
              </button>
              <button class="btn btn--secondary" disabled={busy} onClick={close}>
                Cancel
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
