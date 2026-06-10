// "My tests" list — rendered from IndexedDB (offline-first). A "Sync now" button pushes
// local-only tests to the server and pulls the Tester's tests down; synced tests merge into
// the same store. A failed sync raises an offline toast (work stays saved locally).
import { render } from "preact";
import { useEffect, useState } from "preact/hooks";
import { allTests, type LocalTest } from "../db/testStore";
import { syncAll } from "../sync/syncClient";
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

function TestListApp() {
  const [tests, setTests] = useState<LocalTest[] | null>(null);
  const [syncing, setSyncing] = useState(false);

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

  if (!tests) return <p class="td-muted">Loading…</p>;

  return (
    <div>
      <div style="display:flex;justify-content:flex-end;margin-bottom:var(--space-3)">
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
                  </td>
                  <td class="td-actions">
                    <a class="btn btn--secondary btn--sm" href={`/App/Tests/Wizard?id=${t.id}`}>
                      {t.markedCompleteAt ? "View" : "Continue"}
                    </a>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
