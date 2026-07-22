// "Company tests" — completed tests from every tester in the signed-in tester's Testing Company,
// read-only. ONLINE-ONLY BY DESIGN: this module imports neither the IndexedDB store nor the sync
// client, so a colleague's test can never land on this device. Please keep it that way — the
// absence of those imports IS the guarantee.
//
// There are no per-row actions: nothing here is editable or deletable (reopening a colleague's
// test as a new version would fork the record and stamp their work with your name — re-testing a
// farm is a NEW test). Rows link to the read-only server view, including your own: deep-linking a
// row to the offline wizard would create a blank local test when that id isn't on this device.
import { render } from "preact";
import { useCallback, useEffect, useRef, useState } from "preact/hooks";
import { useServerOnline } from "../connectivity";
import {
  fetchCompanyTests,
  formatCompleted,
  remainingCount,
  testerLabel,
  type CompanyTestRow,
} from "./companyTests";

const PAGE_SIZE = 25;
const SEARCH_DEBOUNCE_MS = 350;

export function mountCompanyTestList(root: HTMLElement): void {
  render(<CompanyTestListApp />, root);
}

function CompanyTestListApp() {
  const online = useServerOnline();
  const [rows, setRows] = useState<CompanyTestRow[] | null>(null);
  const [companyName, setCompanyName] = useState<string | null>(null);
  const [total, setTotal] = useState(0);
  const [query, setQuery] = useState("");
  const [input, setInput] = useState("");
  const [loading, setLoading] = useState(false);
  const [failed, setFailed] = useState(false);
  // Set once the first successful load lands: distinguishes "we never got the list" (show the
  // offline card) from "we have rows but the connection dropped" (keep them on screen).
  const loadedOnce = useRef(false);

  // Debounce typing so a search doesn't fire a request per keystroke on a metered rural link.
  useEffect(() => {
    const t = setTimeout(() => setQuery(input), SEARCH_DEBOUNCE_MS);
    return () => clearTimeout(t);
  }, [input]);

  const load = useCallback(async (q: string, skip: number) => {
    setLoading(true);
    setFailed(false);
    try {
      const page = await fetchCompanyTests({ q, skip, take: PAGE_SIZE });
      setCompanyName(page.companyName);
      setTotal(page.total);
      setRows((prev) => (skip > 0 && prev ? [...prev, ...page.items] : page.items));
      loadedOnce.current = true;
    } catch {
      setFailed(true);
    } finally {
      setLoading(false);
    }
  }, []);

  // Don't fire a doomed request while offline — the offline card explains it better than a
  // failed fetch would. Re-runs when connectivity returns.
  useEffect(() => {
    if (!online) return;
    void load(query, 0);
  }, [query, online, load]);

  const subtitle = companyName
    ? `Completed tests from every tester at ${companyName}. Read-only, and only available while you're online.`
    : "Completed tests from every tester at your company. Read-only, and only available while you're online.";

  // Never loaded and no connection: say why, rather than showing an empty list that reads as
  // "your company has no tests".
  if (!online && !loadedOnce.current) {
    return (
      <div>
        <p class="td-muted">{subtitle}</p>
        <div class="empty">
          <div class="empty__title">Company tests need a connection</div>
          <p class="empty__text">
            Your colleagues' tests are stored on the server, not on this device — so this list only
            works online. Your own tests are always here, signal or not.
          </p>
          <a class="btn btn--secondary" href="/App/Tests/Index">Go to My tests</a>
        </div>
      </div>
    );
  }

  if (failed && !loadedOnce.current) {
    return (
      <div>
        <p class="td-muted">{subtitle}</p>
        <div class="empty">
          <div class="empty__title">Couldn't load company tests</div>
          <p class="empty__text">
            The server didn't answer. Your connection may have dropped. Your own tests are still
            available offline in My tests.
          </p>
          <div class="form-actions">
            <button class="btn" onClick={() => void load(query, 0)}>Try again</button>
            <a class="btn btn--secondary" href="/App/Tests/Index">Go to My tests</a>
          </div>
        </div>
      </div>
    );
  }

  // A tester with no Testing Company: the server returns an empty list with no company name, which
  // is an admin/data problem rather than anything the tester did wrong.
  if (rows !== null && companyName === null && total === 0 && !query) {
    return (
      <div>
        <div class="empty">
          <div class="empty__title">You're not linked to a testing company</div>
          <p class="empty__text">
            Ask your company administrator, or NZMPTA, to add you to your testing company — then
            completed tests from your colleagues will show up here. Your own tests aren't affected.
          </p>
          <a class="btn btn--secondary" href="/App/Tests/Index">Go to My tests</a>
        </div>
      </div>
    );
  }

  const loaded = rows?.length ?? 0;
  const remaining = remainingCount(total, loaded);

  return (
    <div>
      <p class="td-muted">{subtitle}</p>

      <form class="list-filters" onSubmit={(e) => e.preventDefault()}>
        <label class="visually-hidden" for="company-test-search">Search by farm or tester</label>
        <input
          id="company-test-search"
          class="list-search"
          type="search"
          inputMode="search"
          autocomplete="off"
          autocapitalize="off"
          placeholder="Search by farm or tester…"
          value={input}
          onInput={(e) => setInput((e.currentTarget as HTMLInputElement).value)}
        />
        {!online && <span class="badge badge--warning">Offline</span>}
      </form>

      {!online && loadedOnce.current && (
        <div class="alert alert--warning" role="alert">
          ⚠️ <strong>You've gone offline</strong> — this list is from your last connection. Opening a
          colleague's test needs a connection.
        </div>
      )}

      <div role="status" aria-live="polite" class="visually-hidden">
        {rows === null ? "" : `${total} test${total === 1 ? "" : "s"} found`}
      </div>

      {rows === null ? (
        <p class="td-muted">Loading…</p>
      ) : rows.length === 0 ? (
        <div class="empty">
          <div class="empty__title">{query ? "No matching tests" : "No company tests yet"}</div>
          <p class="empty__text">
            {query
              ? `Nothing matched "${query}". Try a farm name, or part of a tester's name.`
              : "Completed tests from everyone at your company will appear here. As soon as a tester marks a test complete and syncs it, you'll see it."}
          </p>
          {query && <button class="btn btn--secondary" onClick={() => setInput("")}>Clear search</button>}
        </div>
      ) : (
        <>
          <ul class="testlist" aria-label="Completed company tests">
            {rows.map((r) => (
              <li key={r.id}>
                {/* Whole card is the tap target — a wet-handed tester on a tablet shouldn't have to
                    hit a small button in a table cell. */}
                <a class="testcard" href={`/App/Tests/View/${r.id}`}>
                  <span class="testcard__farm">{r.farmName || "—"}</span>
                  <span class="testcard__meta">
                    {testerLabel(r)} · {formatCompleted(r.completedAt)}
                  </span>
                  <span class="testcard__badges">
                    {r.isMine && <span class="badge badge--mine">You</span>}
                    {r.version > 1 && <span class="badge">v{r.version}</span>}
                  </span>
                  <span class="testcard__go" aria-hidden="true">›</span>
                </a>
              </li>
            ))}
          </ul>

          <div class="testlist__footer">
            <span class="td-muted">
              {remaining === 0
                ? `Showing all ${total} test${total === 1 ? "" : "s"}`
                : `Showing ${loaded} of ${total} tests`}
            </span>
            {remaining > 0 && (
              <button
                class="btn btn--secondary"
                disabled={loading || !online}
                onClick={() => void load(query, loaded)}
              >
                {!online
                  ? "Offline — can't load more"
                  : loading
                    ? "Loading…"
                    : `Load ${Math.min(PAGE_SIZE, remaining)} more`}
              </button>
            )}
          </div>
        </>
      )}
    </div>
  );
}
