// The admin portal's edit of a completed test — O2 for a Super-Administrator, PRD stories 49–50 for a
// Company Administrator — as the pure steps the wizard takes. The edit is made in memory on a copy of
// the version exactly as the server stores it, and saved as that test's next version
// (POST /api/admin/tests/{id}/versions). The server checks every changed field against the stored
// version and refuses anything outside the editor's role, so these only shape the request: what's
// diffed and sent is the stored record plus the editor's changes, never a display copy of it.
import type { AmendmentRecord, LocalTest } from "../db/testStore";
import { defaultMachineConfiguration } from "../wizard/types";

/** What an administrator may change: every field the wizard edits ("full", Super-Administrator), or
 * the fault summary's recommendations and general comments ("summary", Company Administrator). */
export type EditScope = "full" | "summary";

const SUMMARY_FIELDS = ["recommendations", "notes"] as const;

const FULL_FIELDS = [
  "config",
  "visualFaults",
  "readings",
  "recommendations",
  "dataFields",
  "pulsatorRows",
  "clusterRows",
  "notes",
  "guardsOnPulsators",
  "pulsationPdf",
  "nextTestDate",
] as const;

/** The LocalTest fields an edit in this scope carries into the saved version. */
export function editableFields(scope: EditScope): readonly string[] {
  return scope === "summary" ? SUMMARY_FIELDS : FULL_FIELDS;
}

/**
 * The stored version, shaped for the wizard to edit: the payload as the server holds it, rehydrated
 * the way a tester's device rehydrates a pulled test, falling back to `view` (the read-only copy the
 * viewer already shows) only where the payload has nothing. Starting from the payload, not the view
 * — whose configuration comes from the server's columns — keeps the diff and the saved record to
 * the editor's own changes.
 */
export function draftFromPayload(raw: Record<string, unknown>, view: LocalTest): LocalTest {
  const stored = raw as Partial<LocalTest>;
  return {
    ...view,
    ...stored,
    id: view.id,
    farmName: stored.farmName ?? view.farmName,
    config: stored.config ?? view.config ?? defaultMachineConfiguration(),
    visualFaults: stored.visualFaults ?? {},
    attestations: stored.attestations ?? [],
    readings: stored.readings ?? {},
    recommendations: stored.recommendations ?? {},
    dataFields: stored.dataFields ?? {},
    currentStep: view.currentStep,
    createdAt: stored.createdAt ?? view.createdAt,
    updatedAt: view.updatedAt,
    markedCompleteAt: stored.markedCompleteAt ?? view.markedCompleteAt,
    syncState: "uploaded",
    readonly: false,
    version: typeof stored.version === "number" ? stored.version : view.version ?? 1,
  };
}

/** The edited test as the save request carries it: the stored payload, with this scope's fields
 * taken from the edit and the edit's amendment record appended to the history. */
export function buildAdminPayload(
  raw: Record<string, unknown>,
  edited: LocalTest,
  scope: EditScope,
  record: AmendmentRecord,
): Record<string, unknown> {
  const payload = JSON.parse(JSON.stringify(raw)) as Record<string, unknown>;
  const source = edited as unknown as Record<string, unknown>;
  for (const field of editableFields(scope)) {
    const value = source[field];
    if (value === undefined) delete payload[field];
    else payload[field] = JSON.parse(JSON.stringify(value));
  }
  const history = Array.isArray(raw.amendments) ? (raw.amendments as AmendmentRecord[]) : [];
  payload.amendments = [...history, record];
  return payload;
}

/** Keeps an edit inside its scope: a Company Administrator's change to anything but the
 * recommendations and comments is dropped as it's made (the server would refuse it anyway). */
export function withinScope(scope: EditScope, patch: Partial<LocalTest>): Partial<LocalTest> {
  if (scope === "full") return patch;
  const allowed = new Set<string>(SUMMARY_FIELDS);
  return Object.fromEntries(Object.entries(patch).filter(([key]) => allowed.has(key))) as Partial<LocalTest>;
}
