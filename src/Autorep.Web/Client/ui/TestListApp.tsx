// "My tests" list — rendered from IndexedDB (offline-first). A "Sync now" button pushes
// local-only tests to the server and pulls the Tester's tests down; synced tests merge into
// the same store. A failed sync raises an offline toast (work stays saved locally).
// In-progress tests that have never reached the server can be deleted (with confirmation);
// anything that exists on the server can't be removed from here.
import { render } from "preact";
import { useEffect, useState } from "preact/hooks";
import { allTests, deleteTest, putTest, TESTS_CHANGED_EVENT, type LocalTest } from "../db/testStore";
import { describeStorage, storageReport } from "../storage/durability";
import { syncAll, SessionExpiredError, type SyncResult } from "../sync/syncClient";
import { hasUnfinishedSibling, madeByOther, replacedIds, rivalsOf } from "../versioning/chain";
import { clearRemoved, deletedOnServer, removedTests, type Deletion, type RemovedTest } from "../sync/removals";
import { CalibrationPanel } from "./CalibrationPanel";
import { GuideLink } from "./GuideLink";
import { showToast } from "./toast";

export function mountTestList(root: HTMLElement): void {
  render(
    <>
      <PageHeader />
      <TestListApp />
    </>,
    root,
  );
}

/** Drawn here rather than in Pages/App/Tests/Index.cshtml so the offline shell, which serves this
 * page from the device, renders exactly what the server does. */
function PageHeader() {
  return (
    <div class="page-header">
      <div class="page-header__heading">
        <h1>My tests</h1>
        <p>Machine tests you've created, saved on this device.</p>
      </div>
      <div class="page-header__actions">
        <a class="btn btn--secondary" href="/App/Tests/Company">
          Company tests
        </a>
        <a class="btn" href="/App/Tests/New">
          + New test
        </a>
      </div>
    </div>
  );
}

function syncLabel(state: LocalTest["syncState"]): string {
  switch (state) {
    case "uploaded":
      return "synced";
    case "uploading":
      return "syncing…";
    case "merge-conflict":
      return "conflict";
    default:
      return "on device";
  }
}

/** Deletable: still in progress AND the server has never seen it (delete here is permanent). */
function canDelete(t: LocalTest): boolean {
  return !t.markedCompleteAt && t.syncState === "local-only" && !t.everUploaded;
}

/** The note added to a sync's toast when tests were combined with an edit made on the server, or
 * removed because NZMPTA deleted them. */
export function mergedNote(r: SyncResult): string {
  let note = "";
  if (r.merged) {
    note += ` ${r.merged === 1 ? "1 test was" : `${r.merged} tests were`} changed on the server while you were editing — ` +
      "your changes were combined with theirs as a new version. Check it before you print it again.";
  }
  if (r.removed) {
    note += ` ${r.removed === 1 ? "1 test was" : `${r.removed} tests were`} deleted by NZMPTA and removed from this device.`;
  }
  return note;
}

function when(iso: string | null): string {
  if (!iso) return "";
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleDateString("en-NZ", { day: "numeric", month: "short", year: "numeric" });
}

function TestListApp() {
  const [tests, setTests] = useState<LocalTest[] | null>(null);
  const [syncing, setSyncing] = useState(false);
  const [deleting, setDeleting] = useState<LocalTest | null>(null);
  // Tests NZMPTA deleted: those removed from this device (until the tester says OK), and those
  // still here because they hold unsent edits.
  const [removed, setRemoved] = useState<RemovedTest[]>([]);
  const [deletions, setDeletions] = useState<Record<string, Deletion>>({});

  const [storageLine, setStorageLine] = useState<string | null>(null);

  const reload = async () => {
    setTests((await allTests()).sort((a, b) => b.createdAt.localeCompare(a.createdAt)));
    setRemoved(await removedTests());
    setDeletions(await deletedOnServer());
  };
  const refreshStorage = () => void storageReport().then((r) => setStorageLine(describeStorage(r)));

  useEffect(() => {
    void reload();
    refreshStorage();
    // A first sync stores the tester's history page by page, newest first: re-read as each page
    // lands so the list fills in while the tail is still coming (debounced — a page is many writes).
    let timer: ReturnType<typeof setTimeout> | undefined;
    const onChanged = () => {
      clearTimeout(timer);
      timer = setTimeout(() => void reload(), 250);
    };
    addEventListener(TESTS_CHANGED_EVENT, onChanged);
    return () => {
      clearTimeout(timer);
      removeEventListener(TESTS_CHANGED_EVENT, onChanged);
    };
  }, []);

  const doSync = async () => {
    setSyncing(true);
    try {
      const r = await syncAll();
      await reload();
      if (r.failed > 0) {
        // The rest of the sync did run — say so, so this doesn't read as "nothing synced".
        showToast(
          `Synced ${r.pushed}, pulled ${r.pulled} — but ${r.failed} test${r.failed === 1 ? "" : "s"} ` +
            `couldn't be sent. ${r.failed === 1 ? "It's" : "They're"} still saved here; try again, ` +
            "and contact NZMPTA if it keeps failing.",
          "error",
        );
      } else {
        showToast(`Synced — ${r.pushed} pushed, ${r.pulled} pulled.${mergedNote(r)}`, r.merged || r.removed ? "info" : "success");
      }
    } catch (e) {
      await reload();
      showToast(
        e instanceof SessionExpiredError
          ? "You've been signed out, so nothing could be synced. Sign in again — your work is safe on this device."
          : "Couldn't reach the server — your changes are saved on this device and will sync when you're back online.",
        "error",
      );
    } finally {
      setSyncing(false);
      refreshStorage();
    }
  };

  const doDelete = async (t: LocalTest) => {
    await deleteTest(t.id);
    // If this was a re-edit version and nothing else supersedes its original, unlock the original
    // again so it isn't stranded read-only with no way to edit.
    if (t.supersedesId) {
      const remaining = await allTests();
      if (!remaining.some((x) => x.supersedesId === t.supersedesId)) {
        const orig = remaining.find((x) => x.id === t.supersedesId);
        if (orig?.readonly) await putTest({ ...orig, readonly: false, syncState: "local-only" });
      }
    }
    setDeleting(null);
    await reload();
    showToast(`Deleted "${t.farmName || "Untitled test"}" from this device.`, "info");
  };

  // Reopen a completed test as a new editable version. The original is kept as history and locked
  // (read-only). The new version starts from the original's data but with a FRESH completion record
  // — its attestations + sign-off are re-done, not inherited. (Two devices editing the same
  // completed test each spawn a version; there's no cross-device merge — consistent with the rest
  // of the offline-first model, where the Sync Reconciliation Engine is a later phase.)
  const editAsNewVersion = async (orig: LocalTest) => {
    const now = new Date().toISOString();
    const copy: LocalTest = {
      ...orig,
      id: crypto.randomUUID(),
      // An attachment this device has let go of lives in the ORIGINAL's server copy — say so, or
      // the new version's pointer would name a test the server has never seen.
      pulsationPdf:
        orig.pulsationPdf && !orig.pulsationPdf.base64
          ? { ...orig.pulsationPdf, serverTestId: orig.pulsationPdf.serverTestId ?? orig.id }
          : orig.pulsationPdf,
      version: (orig.version ?? 1) + 1,
      supersedesId: orig.id,
      attestations: [],
      markedCompleteAt: null,
      syncState: "local-only",
      everUploaded: false,
      readonly: false,
      createdAt: now,
      updatedAt: now,
    };
    await putTest(copy);
    await putTest({ ...orig, readonly: true, syncState: "local-only", updatedAt: now });
    location.href = `/App/Tests/Wizard?id=${copy.id}`;
  };

  if (!tests) return <p class="td-muted">Loading…</p>;
  // Replaced by a later version — the tester's own, an administrator's, or an automatic merge.
  const supersededIds = replacedIds(tests);

  return (
    <div>
      {/* The tester's equipment calibration — profile data that follows the tester, with the
          6-week renewal highlight and the expired red alert (testing itself is never blocked). */}
      <CalibrationPanel />

      <div style="display:flex;justify-content:flex-end;gap:var(--space-2);margin:var(--space-4) 0 var(--space-3)">
        <GuideLink variant="button" />
        <button class="btn btn--secondary btn--sm" disabled={syncing} onClick={() => void doSync()}>
          {syncing ? "Syncing…" : "Sync now"}
        </button>
      </div>
      {storageLine && (
        <p class="td-muted" style="margin:0 0 var(--space-3);font-size:0.8125rem;text-align:right" data-storage-line>
          {storageLine}
        </p>
      )}

      {removed.length > 0 && (
        <div class="alert alert--warning" role="status" data-removed-tests>
          🗑 <strong>NZMPTA deleted {removed.length === 1 ? "a test" : `${removed.length} tests`}</strong>, so{" "}
          {removed.length === 1 ? "it has" : "they have"} been removed from this device:
          <ul class="removed-tests">
            {removed.map((r) => (
              <li key={r.id}>
                {r.farmName || "Untitled test"}
                {r.version > 1 ? ` (version ${r.version})` : ""}
                {r.deletedAt ? `, deleted ${when(r.deletedAt)}` : ""}
                {r.reason ? <>: “{r.reason}”</> : ""}
                {r.hadUnsentChanges ? " — the changes you hadn't sent went up first and are kept with it." : ""}
              </li>
            ))}
          </ul>
          <button
            class="btn btn--secondary btn--sm"
            onClick={() => void clearRemoved().then(() => setRemoved([]))}
          >
            OK
          </button>
        </div>
      )}

      {tests.length === 0 ? (
        <div class="empty">
          <div class="empty__title">No tests yet</div>
          <p class="empty__text">Start your first machine test, or sync to pull your existing tests onto this device.</p>
          <a class="btn" href="/App/Tests/New">Start a new test</a>
        </div>
      ) : (
        <div class="table-wrap">
          <table class="table">
            <thead>
              <tr>
                <th>Farm</th>
                <th>Created</th>
                <th>Status</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {tests.map((t) => (
                <tr key={t.id}>
                  <td>{t.farmName || "—"}</td>
                  <td class="td-muted">{new Date(t.createdAt).toLocaleString()}</td>
                  <td>
                    {t.markedCompleteAt ? (
                      <span class="badge badge--success">Complete</span>
                    ) : (
                      <span class="badge badge--warning">In progress</span>
                    )}{" "}
                    <span class="badge">{syncLabel(t.syncState)}</span>
                    {(t.version ?? 1) > 1 && <> <span class="badge">v{t.version}</span></>}
                    {supersededIds.has(t.id) && <> <span class="badge">superseded</span></>}
                    <VersionBadge test={t} tests={tests} />
                    {deletions[t.id] && (
                      <>
                        {" "}
                        <span class="badge badge--danger" title={deletions[t.id].reason ?? undefined}>
                          deleted by NZMPTA
                        </span>
                      </>
                    )}
                  </td>
                  <td class="td-actions">
                    {canDelete(t) && (
                      <button class="btn btn--danger-soft btn--sm" onClick={() => setDeleting(t)}>
                        Delete
                      </button>
                    )}
                    <a class="btn btn--secondary btn--sm" href={`/App/Tests/Wizard?id=${t.id}`}>
                      {t.markedCompleteAt ? "View" : "Continue"}
                    </a>
                    {/* Not while an unfinished edit of the same test is on the device: a second edit
                        would only fork the test again (finish that one — it's combined on sign-off). */}
                    {t.markedCompleteAt && !supersededIds.has(t.id) && !t.readonly && !hasUnfinishedSibling(t, tests) && (
                      <button class="btn btn--secondary btn--sm" onClick={() => void editAsNewVersion(t)}>
                        Edit
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* Catches the tester at the moment they've scrolled their own list and not found what they
          wanted. Deliberately not a count of company tests — that would need a network call on the
          one page that has to work perfectly offline. */}
      {tests.length > 0 && (
        <p class="td-muted" style="margin-top:var(--space-4);font-size:0.8125rem">
          Looking for a colleague's test? Try <a href="/App/Tests/Company">Company tests</a> (needs a
          connection).
        </p>
      )}

      {deleting && (
        <div
          class="modal-overlay open"
          onClick={(e) => {
            if (e.target === e.currentTarget) setDeleting(null);
          }}
        >
          <div class="modal">
            <div class="modal__title">Delete this test?</div>
            <p>
              <strong>{deleting.farmName || "Untitled test"}</strong> only exists on this device — it has never
              been synced. Deleting it is permanent and can't be undone.
            </p>
            <div class="form-actions">
              <button class="btn btn--danger" onClick={() => void doDelete(deleting)}>
                Delete test
              </button>
              <button class="btn btn--secondary" onClick={() => setDeleting(null)}>
                Cancel
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

/** Who else had a hand in this version: an administrator's edit, or an automatic merge — or, on an
 * unfinished one, an edit of the same test made elsewhere, which it's combined with on sign-off. */
function VersionBadge({ test, tests }: { test: LocalTest; tests: readonly LocalTest[] }) {
  const other = madeByOther(test);
  if (other?.merge) return <> <span class="badge" title="Combined automatically with an edit made on the server">combined</span></>;
  if (other) return <> <span class="badge" title={`Made by ${other.amendedByName ?? other.amendedBy ?? "an administrator"} (${other.amendedByRole})`}>edited by admin</span></>;
  if (!test.markedCompleteAt && rivalsOf(test, tests).length > 0) {
    return <> <span class="badge badge--warning" title="Someone else changed this test while you were editing it">changed on the server</span></>;
  }
  return null;
}
