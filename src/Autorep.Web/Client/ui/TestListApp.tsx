// "My tests" list — rendered from IndexedDB (offline-first). Synced/downloaded tests merge into
// the same store, so this one list shows local-only and synced tests alike.
import { render } from "preact";
import { useEffect, useState } from "preact/hooks";
import { allTests, type LocalTest } from "../db/testStore";

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

  useEffect(() => {
    void allTests().then((all) =>
      setTests(all.sort((a, b) => b.createdAt.localeCompare(a.createdAt))),
    );
  }, []);

  if (!tests) return <p class="td-muted">Loading…</p>;

  if (tests.length === 0) {
    return (
      <div class="empty">
        <div class="empty__title">No tests yet</div>
        <p class="empty__text">Start your first machine test to begin building your testing history.</p>
        <a class="btn" href="/App/Tests/New">Start a new test</a>
      </div>
    );
  }

  return (
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
  );
}
