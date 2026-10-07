// Automatic merge of two versions of one test that were both made from the same earlier version —
// in practice a tester's edit made offline and an administrator's made online (PRD §Decisions, sync
// reconciliation). It runs on the tester's device when the server answers its push with a collision
// (sync/syncClient.ts), because the calculated readings and the worded amendment record come from
// this TypeScript. Pure and deterministic: the same three versions always combine the same way.
//
// Field by field — divided exactly as the server's Services/PayloadUnits.cs divides a payload: each
// configuration property, reading, visual check, recommendation and recorded measurement on its own;
// the pulsator and cluster tables and the pump lists as one field each; anything else top-level as
// one field:
//  - changed in one version only → that version's value;
//  - changed in both to the same value → that value;
//  - changed in both, differently → the version that reached the server LAST wins. That is always
//    the incoming one (the head was there first), and the merge record lists every such field with
//    both values.
// Calculated readings aren't merged; they're recomputed from the merged inputs. Who did the test,
// for whom and where is the same in every version of it, so the head's copy stands. Both versions
// stay on record in full: the merged version supersedes the head and names the incoming one.
import type { ChecklistAttestation } from "../wizard/types";
import type { AmendmentRecord, LocalTest, MergeOverlap } from "../db/testStore";
import { computeChanges, computeChangesWithPaths, type PathedChange } from "./amendments";
import { deriveReadings, isDerivedReading } from "../passfail/derived";
import { ownAmendment } from "./chain";

/** Fields held as a map of separately edited entries. */
const KEYED = new Set(["config", "readings", "visualFaults", "recommendations", "dataFields"]);

/** Bookkeeping — set for the merged version below, never merged. */
const BOOKKEEPING = new Set([
  "id", "version", "supersedesId", "mergedFromId", "syncState", "everUploaded", "readonly", "currentStep",
  "createdAt", "updatedAt", "markedCompleteAt", "amendments", "attestations", "deletedOnServer",
]);

/** The same in every version of a test (the server refuses an edit that changes them). */
const FIXED = new Set([
  "farmId", "farmName", "farm", "testedBy", "testingCompanyId", "testingCompanyName",
  "verdicts", "recordedRecommendations", "recordedVisualFaults", "legacy",
]);

/** The rule that settled a field both versions changed: the later arrival at the server wins. */
export const MERGE_RULE_KEPT: MergeOverlap["kept"] = "incoming";

export interface MergeOptions {
  /** Id for the merged version. */
  id: string;
  /** When the merge happened (its sign-off time). */
  now: string;
  /** The signed-in account performing it. */
  mergedBy?: string | null;
}

export interface MergeResult {
  merged: LocalTest;
  /** The fields both versions changed, differently, as payload paths. */
  overlapping: string[];
}

/** Stable text for a value: key order, and absent-vs-null, don't make two values differ. */
function canon(v: unknown): string {
  if (v === null || v === undefined) return "null";
  if (Array.isArray(v)) return `[${v.map(canon).join(",")}]`;
  if (typeof v === "object") {
    const entries = Object.entries(v as Record<string, unknown>)
      .filter(([, x]) => x !== null && x !== undefined)
      .sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0));
    return `{${entries.map(([k, x]) => `${JSON.stringify(k)}:${canon(x)}`).join(",")}}`;
  }
  return JSON.stringify(v);
}

/** The analyser PDF is the same attachment with or without its bytes on this device. */
function comparable(key: string, v: unknown): unknown {
  if (key !== "pulsationPdf" || !v || typeof v !== "object") return v;
  const pdf = v as { name?: unknown; size?: unknown; attachedAt?: unknown };
  return { name: pdf.name, size: pdf.size, attachedAt: pdf.attachedAt };
}

const clone = <T>(v: T): T => (v === undefined ? v : (JSON.parse(JSON.stringify(v)) as T));

/** One field's outcome: the value to keep, and whether both versions had changed it differently. */
function decide(key: string, base: unknown, head: unknown, incoming: unknown): { value: unknown; clash: boolean } {
  const b = canon(comparable(key, base));
  const h = canon(comparable(key, head));
  const i = canon(comparable(key, incoming));
  if (i === b) return { value: head, clash: false };
  if (h === b || h === i) return { value: incoming, clash: false };
  return { value: MERGE_RULE_KEPT === "incoming" ? incoming : head, clash: true };
}

export function mergeVersions(base: LocalTest, head: LocalTest, incoming: LocalTest, opts: MergeOptions): MergeResult {
  const b = base as unknown as Record<string, unknown>;
  const h = head as unknown as Record<string, unknown>;
  const i = incoming as unknown as Record<string, unknown>;
  const merged = clone(h);
  const overlapping: string[] = [];

  for (const key of new Set([...Object.keys(b), ...Object.keys(h), ...Object.keys(i)])) {
    if (BOOKKEEPING.has(key) || FIXED.has(key)) continue;
    if (KEYED.has(key)) {
      const bm = (b[key] ?? {}) as Record<string, unknown>;
      const hm = (h[key] ?? {}) as Record<string, unknown>;
      const im = (i[key] ?? {}) as Record<string, unknown>;
      const out = clone(hm);
      for (const inner of new Set([...Object.keys(bm), ...Object.keys(hm), ...Object.keys(im)])) {
        if (key === "readings" && isDerivedReading(inner)) continue;
        const { value, clash } = decide(inner, bm[inner], hm[inner], im[inner]);
        if (value === undefined || value === null) delete out[inner];
        else out[inner] = clone(value);
        if (clash) overlapping.push(`${key}.${inner}`);
      }
      merged[key] = out;
      continue;
    }
    const { value, clash } = decide(key, b[key], h[key], i[key]);
    if (value === undefined) delete merged[key];
    else merged[key] = clone(value);
    if (clash) overlapping.push(key);
  }

  const result = merged as unknown as LocalTest;
  // Calculated readings follow the merged inputs, whichever versions they came from.
  result.readings = deriveReadings(result.config, result.readings ?? {});
  // An attachment taken from the head without its bytes: the head's server copy holds them.
  if (result.pulsationPdf && !result.pulsationPdf.base64 && canon(comparable("pulsationPdf", result.pulsationPdf)) === canon(comparable("pulsationPdf", head.pulsationPdf))) {
    result.pulsationPdf = { ...result.pulsationPdf, onServer: true, serverTestId: head.pulsationPdf?.serverTestId ?? head.id };
  }

  const version = Math.max(head.version ?? 1, incoming.version ?? 1) + 1;
  const attestations = unionAttestations(head.attestations, incoming.attestations);
  // A merge isn't a sign-off: the combined version is as complete as the later of the two it
  // combines (the report's "Tested" date). When the merge happened is its amendment record's.
  const completedAt = [head.markedCompleteAt, incoming.markedCompleteAt]
    .filter((d): d is string => Boolean(d))
    .sort((a, b) => Date.parse(a) - Date.parse(b))
    .at(-1) ?? opts.now;
  const shaped: LocalTest = {
    ...result,
    id: opts.id,
    version,
    supersedesId: head.id,
    mergedFromId: incoming.id,
    attestations,
    createdAt: opts.now,
    updatedAt: opts.now,
    markedCompleteAt: completedAt,
    currentStep: "ReviewSignOff",
    syncState: "local-only",
    everUploaded: false,
    readonly: false,
    amendments: head.amendments ? clone(head.amendments) : [],
  };

  const record: AmendmentRecord = {
    version,
    amendedAt: opts.now,
    ...(opts.mergedBy ? { amendedBy: opts.mergedBy } : {}),
    amendedByRole: "Automatic merge",
    baseVersion: head.version ?? 1,
    baseCompletedAt: head.markedCompleteAt ?? null,
    changes: computeChanges(head, shaped),
    merge: {
      headVersion: head.version ?? 1,
      headBy: authorName(head),
      fromVersion: incoming.version ?? 1,
      fromId: incoming.id,
      fromBy: authorName(incoming),
      fromCompletedAt: incoming.markedCompleteAt ?? null,
      overlaps: describeOverlaps(overlapping, base, head, incoming),
    },
  };
  shaped.amendments = [...(shaped.amendments ?? []), record];
  return { merged: shaped, overlapping };
}

/** Who made a version, for the merge record: its own amendment's author, else the tester on it. */
function authorName(t: LocalTest): string | undefined {
  const own = ownAmendment(t);
  return own?.amendedByName ?? own?.amendedBy ?? t.testedBy?.name ?? undefined;
}

/** Both versions' attestations, each once, oldest first: neither version's evidence is dropped. */
function unionAttestations(a: ChecklistAttestation[] = [], b: ChecklistAttestation[] = []): ChecklistAttestation[] {
  const seen = new Set<string>();
  const out: ChecklistAttestation[] = [];
  for (const x of [...a, ...b]) {
    const key = canon(x);
    if (seen.has(key)) continue;
    seen.add(key);
    out.push(clone(x));
  }
  return out.sort((x, y) => x.attestedAt.localeCompare(y.attestedAt));
}

/**
 * The overlapping fields in words, one row per labelled change (a table or pump list is one field
 * but several rows): the value before, each version's value, and which one was kept.
 */
function describeOverlaps(paths: string[], base: LocalTest, head: LocalTest, incoming: LocalTest): MergeOverlap[] {
  if (paths.length === 0) return [];
  const wanted = new Set(paths);
  const byLabel = (changes: PathedChange[]) =>
    new Map(changes.filter((c) => wanted.has(c.path)).map((c) => [`${c.path}\u0000${c.label}`, c]));
  const headSide = byLabel(computeChangesWithPaths(base, head));
  const incomingSide = byLabel(computeChangesWithPaths(base, incoming));

  const rows: MergeOverlap[] = [];
  for (const key of new Set([...headSide.keys(), ...incomingSide.keys()])) {
    const hc = headSide.get(key);
    const ic = incomingSide.get(key);
    const any = (hc ?? ic)!;
    const previous = any.from;
    rows.push({
      section: any.section,
      label: any.label,
      previous,
      head: hc?.to ?? previous,
      incoming: ic?.to ?? previous,
      kept: MERGE_RULE_KEPT,
    });
  }
  return rows;
}
