// API client + pure helpers for the "Company tests" list (GET /api/tests).
//
// Deliberately has NO dependency on the IndexedDB store or the sync client: colleagues' tests are
// read online and never cached on this device. Keeping the fetch and the formatting here (rather
// than inside the component) also keeps them unit-testable — the client test suite is pure-logic.

export interface CompanyTestRow {
  id: string;
  farmName: string;
  testerName: string | null;
  completedAt: string;
  version: number;
  isMine: boolean;
}

export interface CompanyTestsPage {
  companyName: string | null;
  total: number;
  items: CompanyTestRow[];
}

export interface CompanyTestsQuery {
  q?: string;
  skip?: number;
  take?: number;
}

/** Builds the /api/tests query string, omitting empty/default values so the URL stays readable. */
export function buildTestsQuery({ q, skip, take }: CompanyTestsQuery): string {
  const params = new URLSearchParams();
  const term = q?.trim();
  if (term) params.set("q", term);
  if (skip && skip > 0) params.set("skip", String(skip));
  if (take) params.set("take", String(take));
  const s = params.toString();
  return s ? `?${s}` : "";
}

/** The owner's name for display. Falls back to a neutral label rather than an email address —
 * testers don't recognise each other's logins, and an email is PII the list needn't spread. */
export function testerLabel(row: CompanyTestRow): string {
  if (row.isMine) return "You";
  const name = row.testerName?.trim();
  return name ? name : "A tester at your company";
}

/** Completion date, e.g. "14 Mar 2026". Deliberately not toLocaleString(): that yields
 * "14/03/2026, 09:31:00", which is dense and ambiguous between DD/MM and MM/DD. */
export function formatCompleted(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return d.toLocaleDateString(undefined, { day: "2-digit", month: "short", year: "numeric" });
}

/** How many rows are still unfetched — drives the "Load more" affordance. */
export function remainingCount(total: number, loaded: number): number {
  return Math.max(0, total - loaded);
}

export async function fetchCompanyTests(query: CompanyTestsQuery): Promise<CompanyTestsPage> {
  const res = await fetch(`/api/tests${buildTestsQuery(query)}`, {
    headers: { Accept: "application/json" },
  });
  if (!res.ok) throw new Error(`Company tests failed (${res.status})`);
  return (await res.json()) as CompanyTestsPage;
}
