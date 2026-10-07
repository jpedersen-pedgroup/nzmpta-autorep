// The admin viewer's "History & audit" drawer (PRD stories 68–69): every version of the test with who
// made it and what each changed, field by field (the amendment records the versions carry), the sync
// conflicts between them, administrators' actions from the audit log, and — for the version on screen —
// whether each visual check was verified item by item or confirmed with "Check all as verified".
// Online-only, like the rest of the admin portal: it reads /api/admin/tests/{id}/history.
import { useEffect, useRef, useState } from "preact/hooks";
import type { AmendmentRecord, LocalTest } from "../db/testStore";
import {
  bulkConfirmedSections,
  checklistVerification,
  type ItemVerificationRow,
  type SectionVerification,
} from "../versioning/attestations";
import type { ChecklistAttestation } from "./types";
import { when } from "./VersionBanners";
import { STEP_SHORT_LABELS } from "./wizardProgress";

interface HistoryVersion {
  id: string;
  version: number;
  createdAt: string;
  markedCompleteAt: string | null;
  author: string | null;
  authorKind: "tester" | "admin" | "merge";
  isCurrent: boolean;
  isDeleted: boolean;
  /** The version's own amendment record — null on an original. */
  amendment: AmendmentRecord | null;
  attestations: ChecklistAttestation[] | null;
}

/** Two versions made from the same one. They share a version number, so they're named by id. */
interface HistoryConflict {
  id: string;
  status: "Pending" | "Merged";
  detectedOn: "Push" | "Save";
  detectedAt: string;
  resolvedAt: string | null;
  baseId: string | null;
  headId: string | null;
  incomingId: string | null;
  mergedId: string | null;
  /** Payload paths; the merged version's record names them in words. */
  overlappingFields: string[];
}

interface HistoryEvent {
  at: string;
  actor: string | null;
  operation: string;
  version: number | null;
  reason: string | null;
}

export interface TestHistory {
  versions: HistoryVersion[];
  conflicts: HistoryConflict[];
  events: HistoryEvent[];
}

type Load = { state: "loading" } | { state: "failed"; message: string } | { state: "ready"; history: TestHistory };

const plural = (n: number, word: string) => `${n} ${word}${n === 1 ? "" : "s"}`;

/** The drawer. `test` is the version on screen as stored — not an edit in progress. */
export function AuditPanel({ testId, test, onClose }: { testId: string; test: LocalTest; onClose(): void }) {
  const [load, setLoad] = useState<Load>({ state: "loading" });
  const closeRef = useRef<HTMLButtonElement>(null);
  const onCloseRef = useRef(onClose);
  onCloseRef.current = onClose;

  useEffect(() => {
    let live = true;
    void (async () => {
      try {
        const res = await fetch(`/api/admin/tests/${testId}/history`, {
          redirect: "manual",
          headers: { Accept: "application/json" },
        });
        if (!live) return;
        if (res.ok) setLoad({ state: "ready", history: (await res.json()) as TestHistory });
        else if (res.status === 401 || res.status === 403 || res.type === "opaqueredirect")
          setLoad({ state: "failed", message: "You've been signed out. Sign in again, then reopen the history." });
        else setLoad({ state: "failed", message: `The history couldn't be loaded (HTTP ${res.status}).` });
      } catch {
        if (live) setLoad({ state: "failed", message: "The server couldn't be reached. Try again when you're back online." });
      }
    })();
    return () => {
      live = false;
    };
  }, [testId]);

  // Focus moves into the drawer once, when it opens; Escape closes it.
  useEffect(() => {
    closeRef.current?.focus();
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onCloseRef.current();
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, []);

  const viewing = load.state === "ready" ? load.history.versions.find((x) => x.id === testId) : undefined;
  return (
    <div
      class="drawer-overlay"
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <aside class="drawer" role="dialog" aria-modal="true" aria-labelledby="audit-title" data-audit-panel>
        <header class="drawer__head">
          <h2 class="drawer__title" id="audit-title">History &amp; audit</h2>
          <button ref={closeRef} class="drawer__close" aria-label="Close" onClick={onClose}>
            ×
          </button>
        </header>
        <div class="drawer__body">
          {load.state === "loading" && <p class="td-muted">Loading the history…</p>}
          {load.state === "failed" && <p class="admin-edit__problem" role="alert">{load.message}</p>}
          {load.state === "ready" && (
            <>
              <Versions versions={load.history.versions} viewingId={testId} />
              {load.history.conflicts.length > 0 && (
                <Conflicts conflicts={load.history.conflicts} versions={load.history.versions} />
              )}
              <Checklist test={test} version={viewing} />
              {load.history.events.length > 0 && <Events events={load.history.events} />}
            </>
          )}
        </div>
      </aside>
    </div>
  );
}

const MADE: Record<HistoryVersion["authorKind"], string> = { tester: "signed off", admin: "saved", merge: "combined" };

function kindLabel(x: HistoryVersion): string {
  if (x.authorKind === "merge") return "Automatic merge";
  if (x.authorKind === "admin") return x.amendment?.amendedByRole ?? "Administrator";
  return x.version === 1 ? "Original" : "Tester's amendment";
}

function Versions({ versions, viewingId }: { versions: HistoryVersion[]; viewingId: string }) {
  return (
    <section class="audit-section" data-audit="versions">
      <h3 class="audit-section__title">Versions</h3>
      <ol class="audit-timeline">
        {versions.map((x) => {
          const a = x.amendment;
          const bulk = bulkConfirmedSections(x.attestations ?? []);
          return (
            <li key={x.id} class="audit-version" data-version={x.version} data-kind={x.authorKind}>
              <div class="audit-version__head">
                <strong>Version {x.version}</strong>
                <span class={`badge${x.authorKind === "tester" ? "" : " badge--info"}`}>{kindLabel(x)}</span>
                {x.isCurrent && <span class="badge badge--success">Current</span>}
                {x.id === viewingId && <span class="badge">On screen</span>}
                {x.isDeleted && <span class="badge badge--danger">Deleted</span>}
              </div>
              <div class="audit-version__meta">
                {x.author ?? "Unknown"} ·{" "}
                {x.markedCompleteAt
                  ? `${MADE[x.authorKind]} ${when(a?.amendedAt ?? x.markedCompleteAt)}`
                  : `started ${when(x.createdAt)} — not signed off yet`}
              </div>
              {a?.reason && <p class="audit-version__reason">“{a.reason}”</p>}
              {a?.merge && <MergeSummary record={a} />}
              {a && <Changes record={a} />}
              {bulk > 0 && (
                <p class="audit-version__note">
                  “Check all as verified” used on {plural(bulk, "checklist section")}.
                </p>
              )}
              {x.id !== viewingId && (
                <a class="audit-version__open" href={`/Admin/Tests/View/${x.id}`}>
                  Open version {x.version}
                </a>
              )}
            </li>
          );
        })}
      </ol>
    </section>
  );
}

/** What a version changed from the one it replaced, as fixed when it was made. */
function Changes({ record }: { record: AmendmentRecord }) {
  if (record.baseUnavailable)
    return (
      <p class="audit-version__note">
        No field-by-field record: version {record.baseVersion} wasn't on the device when this one was signed off.
      </p>
    );
  const changes = record.changes ?? [];
  if (changes.length === 0)
    return <p class="audit-version__note">No field changed from version {record.baseVersion}.</p>;
  return (
    <details class="audit-changes" open={changes.length <= 6}>
      <summary>
        {plural(changes.length, "change")} from version {record.baseVersion}
      </summary>
      <table class="audit-table">
        <thead>
          <tr>
            <th>Field</th>
            <th>Before</th>
            <th>After</th>
          </tr>
        </thead>
        <tbody>
          {changes.map((c, i) => (
            <tr key={i}>
              <td>
                <span class="audit-table__section">{c.section}</span> {c.label}
              </td>
              <td>{c.from}</td>
              <td>{c.to}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </details>
  );
}

function MergeSummary({ record }: { record: AmendmentRecord }) {
  const m = record.merge!;
  return (
    <div class="audit-merge">
      Combined version {m.headVersion}
      {m.headBy ? ` (${m.headBy})` : ""} and version {m.fromVersion}
      {m.fromBy ? ` (${m.fromBy})` : ""}, both made from version {record.baseVersion}.{" "}
      {m.overlaps.length === 0 ? (
        "They changed different fields, so everything from both was kept."
      ) : (
        <>
          Both changed {plural(m.overlaps.length, "field")}; the later of the two to reach the server was kept:
          <table class="audit-table">
            <thead>
              <tr>
                <th>Field</th>
                <th>Before</th>
                <th>Version {m.headVersion}</th>
                <th>Version {m.fromVersion}</th>
              </tr>
            </thead>
            <tbody>
              {m.overlaps.map((o, i) => (
                <tr key={i}>
                  <td>
                    <span class="audit-table__section">{o.section}</span> {o.label}
                  </td>
                  <td>{o.previous}</td>
                  <td class={o.kept === "head" ? "audit-table__kept" : ""}>{o.head}</td>
                  <td class={o.kept === "incoming" ? "audit-table__kept" : ""}>{o.incoming}</td>
                </tr>
              ))}
            </tbody>
          </table>
          <span class="audit-version__note">The kept value is highlighted.</span>
        </>
      )}
    </div>
  );
}

function Conflicts({ conflicts, versions }: { conflicts: HistoryConflict[]; versions: HistoryVersion[] }) {
  const byId = (id: string | null) => versions.find((x) => x.id === id);
  // "version 2 (Jane Smith)": the two versions that collided carry the same number.
  const name = (id: string | null) => {
    const x = byId(id);
    return x ? `version ${x.version}${x.author ? ` (${x.author})` : ""}` : "an earlier version";
  };
  return (
    <section class="audit-section" data-audit="conflicts">
      <h3 class="audit-section__title">Sync conflicts</h3>
      <ul class="audit-list">
        {conflicts.map((c) => {
          const merged = c.status === "Merged";
          const labels = byId(c.mergedId)?.amendment?.merge?.overlaps.map((o) => o.label);
          const fields = labels ?? c.overlappingFields;
          const head = name(c.headId);
          return (
            <li key={c.id} class="audit-conflict" data-conflict-status={c.status.toLowerCase()}>
              <span class={`badge ${merged ? "badge--success" : "badge--warning"}`}>
                {merged ? "Combined" : "Not combined yet"}
              </span>{" "}
              {head[0].toUpperCase() + head.slice(1)} and {name(c.incomingId)} were both made from {name(c.baseId)}
              {c.detectedOn === "Save"
                ? ` — found when ${head} was saved while the tester was still working on theirs`
                : " — found when the tester's device sent theirs"}
              , {when(c.detectedAt)}.{" "}
              {merged ? (
                <>
                  Combined as{" "}
                  {c.mergedId ? <a href={`/Admin/Tests/View/${c.mergedId}`}>{name(c.mergedId)}</a> : "a new version"}
                  {c.resolvedAt ? ` on ${when(c.resolvedAt)}` : ""}.
                </>
              ) : (
                <>
                  The tester's device combines them when theirs is signed off and synced, and tells the tester
                  it has.
                </>
              )}
              {fields.length > 0 && <div class="audit-conflict__fields">Changed in both: {fields.join(", ")}</div>}
            </li>
          );
        })}
      </ul>
    </section>
  );
}

const VERIFIED_TEXT: Record<ItemVerificationRow["verified"], string> = {
  bulk: "confirmed with “Check all as verified”",
  individual: "checked individually",
  unchecked: "not checked",
};

/** PRD story 69: how each visual check of the version on screen was verified. */
function Checklist({ test, version }: { test: LocalTest; version?: HistoryVersion }) {
  const sections = checklistVerification(test);
  const bulkSections = sections.filter((s) => s.attestation).length;
  const individual = sections.reduce((n, s) => n + s.items.filter((i) => i.verified === "individual").length, 0);
  return (
    <section class="audit-section" data-audit="checklist">
      <h3 class="audit-section__title">How the visual checks were verified{version ? ` — version ${version.version}` : ""}</h3>
      {sections.length === 0 ? (
        <p class="td-muted">No visual checks are recorded on this version.</p>
      ) : (
        <>
          <p class="audit-version__note" data-checklist-summary>
            {bulkSections === 0
              ? `Every section was checked item by item (${plural(individual, "check")}).`
              : `${plural(bulkSections, "section")} of ${sections.length} confirmed with “Check all as verified”; ${plural(
                  individual,
                  "check",
                )} set individually (faults always are).`}
            {version && version.authorKind !== "tester"
              ? " An administrator's edit or a merge doesn't redo the inspection: these are the tester's checks."
              : ""}
          </p>
          {sections.map((s) => (
            <ChecklistSectionRow key={`${s.step}/${s.key}`} section={s} />
          ))}
        </>
      )}
    </section>
  );
}

function ChecklistSectionRow({ section }: { section: SectionVerification }) {
  const count = (k: ItemVerificationRow["verified"]) => section.items.filter((i) => i.verified === k).length;
  const faults = section.items.filter((i) => i.status === "fault").length;
  return (
    <details class="audit-check" data-section={section.key} data-attested={section.attestation ? "true" : "false"}>
      <summary>
        <span class="audit-check__title">
          {STEP_SHORT_LABELS[section.step]} · {section.title}
        </span>{" "}
        {section.attestation ? (
          <span class="badge badge--warning">Check all as verified · {when(section.attestation.attestedAt)}</span>
        ) : (
          <span class="badge">Item by item</span>
        )}
        <span class="audit-check__counts">
          {[
            count("bulk") ? `${count("bulk")} in bulk` : "",
            count("individual") ? `${count("individual")} individually` : "",
            faults ? plural(faults, "fault") : "",
            count("unchecked") ? `${count("unchecked")} not checked` : "",
          ]
            .filter(Boolean)
            .join(" · ")}
        </span>
      </summary>
      {section.attestation && <p class="audit-check__attest">“{section.attestation.text}”</p>}
      <ul class="audit-check__items">
        {section.items.map((i) => (
          <li key={i.key} data-item={i.key} data-verified={i.verified}>
            <span>{i.label}</span>
            <span class="audit-check__status">
              {i.status === "fault" ? `Fault${i.detail ? ` (${i.detail})` : ""}` : i.status === "ok" ? "OK" : "—"}
              {i.status ? ` · ${VERIFIED_TEXT[i.verified]}` : ""}
            </span>
          </li>
        ))}
      </ul>
    </details>
  );
}

const EVENT_TEXT: Record<string, (e: HistoryEvent) => string> = {
  AdminVersionCreated: (e) => `saved ${e.version ? `version ${e.version}` : "a new version"}`,
  SoftDeleted: () => "deleted the test",
  Restored: () => "restored the test",
};

function Events({ events }: { events: HistoryEvent[] }) {
  return (
    <section class="audit-section" data-audit="events">
      <h3 class="audit-section__title">Administrator actions</h3>
      <ul class="audit-list">
        {events.map((e, i) => (
          <li key={i} data-operation={e.operation}>
            {when(e.at)} — <strong>{e.actor ?? "Unknown"}</strong> {(EVENT_TEXT[e.operation] ?? (() => e.operation))(e)}
            {e.reason ? <>: “{e.reason}”</> : ""}
          </li>
        ))}
      </ul>
    </section>
  );
}
