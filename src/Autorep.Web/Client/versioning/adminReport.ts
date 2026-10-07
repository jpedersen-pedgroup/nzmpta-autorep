// An administrator's version keeps the report the admin viewer made for it right after the save — the
// tester's engine, the full report with the analyser's pages (PRD 73) — as a tester's device keeps the
// one it signed off (sync/finalReportUpload.ts): PUT /api/admin/tests/{id}/final-report. Pinned to the
// moment of the save, so making it again gives the same bytes and a retry changes nothing. The admin
// portal is online-only, so there's no queue: it goes now, or the viewer says so and offers a retry.
import type { LocalTest, TesterDetails } from "../db/testStore";
import { reportPdfBytes, type ReportBranding, type ReportPdf } from "../report/testSummaryPdf";
import { MAX_FINAL_REPORT_BYTES } from "../sync/finalReportUpload";
import { ownAmendment } from "./chain";

export type AdminReportOutcome =
  | { kind: "stored"; sizeBytes: number }
  | { kind: "signed-out" }
  | { kind: "failed"; message: string };

/** What the viewer shows about it: under way, or how it went. */
export type SavedReportState = { kind: "storing" } | AdminReportOutcome;

export interface AdminReportDeps {
  make(test: LocalTest, branding: ReportBranding | undefined, testerFallback: TesterDetails | null | undefined): Promise<ReportPdf>;
  put(testId: string, bytes: Uint8Array): Promise<Response>;
}

const defaults: AdminReportDeps = {
  make: (test, branding, testerFallback) =>
    reportPdfBytes(test, {
      branding,
      testerFallback,
      serverView: true,
      generatedAt: ownAmendment(test)?.amendedAt ?? test.markedCompleteAt ?? undefined,
    }),
  put: (testId, bytes) =>
    fetch(`/api/admin/tests/${encodeURIComponent(testId)}/final-report`, {
      method: "PUT",
      // A redirect would be the sign-in page — never follow it and call that success.
      redirect: "manual",
      headers: { "Content-Type": "application/pdf" },
      body: new Blob([bytes as BlobPart], { type: "application/pdf" }),
    }),
};

/** Makes the version's report and stores it on the server. `test` is the version as the server
 * view holds it (its id is the server's). */
export async function storeAdminVersionReport(
  test: LocalTest,
  branding: ReportBranding | undefined,
  testerFallback: TesterDetails | null | undefined,
  deps: AdminReportDeps = defaults,
): Promise<AdminReportOutcome> {
  let pdf: ReportPdf;
  try {
    pdf = await deps.make(test, branding, testerFallback);
  } catch {
    return { kind: "failed", message: "The report couldn't be made in this browser." };
  }
  // Without pages it should have — the analyser PDF couldn't be fetched, or appended — it isn't this
  // version's report; a PDF that can never be read goes without them, as the tester's would.
  if (pdf.analyser === "unreachable" || pdf.analyser === "merger-unavailable")
    return { kind: "failed", message: "The pulsation analyser PDF couldn't be added to the report just now." };
  if (pdf.bytes.length > MAX_FINAL_REPORT_BYTES)
    return { kind: "failed", message: "The report is too large to keep on the server." };

  let res: Response;
  try {
    res = await deps.put(test.id, pdf.bytes);
  } catch {
    return { kind: "failed", message: "The server couldn't be reached." };
  }
  if (res.status === 401 || res.status === 403 || res.type === "opaqueredirect") return { kind: "signed-out" };
  if (res.ok) return { kind: "stored", sizeBytes: pdf.bytes.length };
  const body = (await res.json().catch(() => null)) as { message?: string } | null;
  return { kind: "failed", message: body?.message ?? `The server didn't keep it (HTTP ${res.status}).` };
}
