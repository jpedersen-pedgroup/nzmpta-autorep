// "My tests" list — rendered from IndexedDB (offline-first). A "Sync now" button pushes
// local-only tests to the server and pulls the Tester's tests down; synced tests merge into
// the same store. A failed sync raises an offline toast (work stays saved locally).
// In-progress tests that have never reached the server can be deleted (with confirmation);
// anything that exists on the server can't be removed from here.
import { render } from "preact";
import { useEffect, useState } from "preact/hooks";
import { allTests, deleteTest, putTest, type LocalTest } from "../db/testStore";
import { syncAll } from "../sync/syncClient";
import { CalibrationPanel } from "./CalibrationPanel";
import { showToast } from "./toast";

export function mountTestList(root: HTMLElement): void {
  render(<TestListApp />, root);
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

function TestListApp() {
  const [tests, setTests] = useState<LocalTest[] | null>(null);
  const [syncing, setSyncing] = useState(false);
  const [deleting, setDeleting] = useState<LocalTest | null>(null);

  const reload = async () =>
    setTests((await allTests()).sort((a, b) => b.createdAt.localeCompare(a.createdAt)));

  useEffect(() => {
    void reload();
  }, []);

  const doSync = async () => {
    setSyncing(true);
    try {
      const r = await syncAll();
      await reload();
      showToast(`Synced — ${r.pushed} pushed, ${r.pulled} pulled.`, "success");
    } catch {
      showToast(
        "Couldn't reach the server — your changes are saved on this device and will sync when you're back online.",
        "error",
      );
    } finally {
      setSyncing(false);
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
  const supersededIds = new Set(tests.map((t) => t.supersedesId).filter(Boolean) as string[]);

  return (
    <div>
      {/* The tester's equipment calibration — profile data that follows the tester, with the
          6-week renewal highlight and the expired red alert (testing itself is never blocked). */}
      <CalibrationPanel />

      <div style="display:flex;justify-content:flex-end;margin:var(--space-4) 0 var(--space-3)">
        <button class="btn btn--secondary btn--sm" disabled={syncing} onClick={() => void doSync()}>
          {syncing ? "Syncing…" : "Sync now"}
        </button>
      </div>

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
                    {t.markedCompleteAt && !supersededIds.has(t.id) && !t.readonly && (
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
