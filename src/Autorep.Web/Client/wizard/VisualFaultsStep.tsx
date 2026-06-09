// Reusable visual-faults checklist step (pre-start and running). Each section is a tab with its
// own "Check all as verified" (scoped to that section). Each item is OK / Fault / blank with a
// fault severity + note.
import { useState } from "preact/hooks";
import type { FaultSeverity, VisualFaultEntry } from "./types";
import { type ChecklistItem, type ChecklistSection, checklistComplete } from "./visualChecklist";
import { Tabs } from "../ui/Tabs";
import { faultObservationsFor } from "../reference/lookups";

interface Props {
  title: string;
  sections: ChecklistSection[];
  entries: Record<string, VisualFaultEntry>;
  onSetEntry: (key: string, entry: VisualFaultEntry | null) => void;
  /** Apply "check all as verified" to a single section (by key). */
  onCheckAll: (sectionKey: string) => void;
  /** Section keys already attested. */
  attestedSections: string[];
  /** Data-capture field values (sizes/diameters), keyed by item key. */
  dataValues: Record<string, string>;
  onSetData: (key: string, value: string) => void;
  guards?: { value: boolean; onChange: (v: boolean) => void };
}

const ATTEST_TEXT =
  "I have inspected all items in this section and confirm they have been seen, tested and are in order.";

function ItemRow({
  item,
  entry,
  onSetEntry,
  dataValue,
  onSetData,
}: {
  item: ChecklistItem;
  entry?: VisualFaultEntry;
  onSetEntry: (key: string, entry: VisualFaultEntry | null) => void;
  dataValue?: string;
  onSetData: (key: string, value: string) => void;
}) {
  if (item.data) {
    return (
      <div class="checkitem">
        <div class="checkitem__name">{item.label}</div>
        <input
          class="checkitem__data"
          type="text"
          placeholder={item.unit ?? ""}
          value={dataValue ?? ""}
          onInput={(e) => onSetData(item.key, (e.currentTarget as HTMLInputElement).value)}
        />
      </div>
    );
  }
  return (
    <div>
      <div class="checkitem">
        <div class="checkitem__name">{item.label}</div>
        <div class="checkitem__actions">
          <button
            class={"ok" + (entry?.status === "ok" ? " on" : "")}
            onClick={() => onSetEntry(item.key, entry?.status === "ok" ? null : { status: "ok" })}
          >
            OK
          </button>
          <button
            class={"bad" + (entry?.status === "fault" ? " on" : "")}
            onClick={() =>
              onSetEntry(item.key, entry?.status === "fault" ? null : { status: "fault", severity: "Major" })
            }
          >
            Fault
          </button>
        </div>
      </div>
      {entry?.status === "fault" && (
        <div class="fault-detail">
          {faultObservationsFor(item.lookup).length > 0 && (
            <select
              class="fault-detail__obs"
              value={entry.observation ?? ""}
              onChange={(e) =>
                onSetEntry(item.key, {
                  ...entry,
                  observation: (e.currentTarget as HTMLSelectElement).value || undefined,
                })
              }
            >
              <option value="">— select fault —</option>
              {faultObservationsFor(item.lookup).map((o) => (
                <option key={o} value={o}>
                  {o}
                </option>
              ))}
            </select>
          )}
          <select
            value={entry.severity ?? "Major"}
            onChange={(e) =>
              onSetEntry(item.key, { ...entry, severity: (e.currentTarget as HTMLSelectElement).value as FaultSeverity })
            }
          >
            <option value="Critical">Critical</option>
            <option value="Major">Major</option>
            <option value="Minor">Minor</option>
          </select>
          <input
            type="text"
            placeholder="Note (optional)"
            value={entry.note ?? ""}
            onInput={(e) => onSetEntry(item.key, { ...entry, note: (e.currentTarget as HTMLInputElement).value })}
          />
        </div>
      )}
    </div>
  );
}

export function VisualFaultsStep({
  title,
  sections,
  entries,
  onSetEntry,
  onCheckAll,
  attestedSections,
  dataValues,
  onSetData,
  guards,
}: Props) {
  const [confirming, setConfirming] = useState<string | null>(null);

  const tabs = sections.map((sec) => ({
    key: sec.key,
    label: sec.title,
    content: (
      <div>
        {sec.items.map((it) => (
          <ItemRow
            key={it.key}
            item={it}
            entry={entries[it.key]}
            onSetEntry={onSetEntry}
            dataValue={dataValues[it.key]}
            onSetData={onSetData}
          />
        ))}
        <div style="display:flex;align-items:center;gap:var(--space-3);margin-top:var(--space-4)">
          <button class="btn btn--secondary btn--sm" onClick={() => setConfirming(sec.key)}>
            ✓ Check all as verified{attestedSections.includes(sec.key) ? " · attested" : ""}
          </button>
          {checklistComplete([sec], entries) && (
            <span style="color:var(--success);font-size:0.85rem">Section complete</span>
          )}
        </div>
      </div>
    ),
  }));

  return (
    <div class="card">
      <div class="card__title">
        {title} <small class="card__hint">Mark each item OK, or log a fault. Items left blank are assumed not present.</small>
      </div>

      {guards && (
        <label class="form-check" style="margin-bottom:var(--space-3)">
          <input
            type="checkbox"
            checked={guards.value}
            onChange={(e) => guards.onChange((e.currentTarget as HTMLInputElement).checked)}
          />
          <span>Guards installed on pulsators</span>
        </label>
      )}

      <Tabs tabs={tabs} />

      {confirming && (
        <div
          class="modal-overlay open"
          onClick={(e) => {
            if (e.target === e.currentTarget) setConfirming(null);
          }}
        >
          <div class="modal">
            <div class="modal__title">Confirm: check all as verified</div>
            <p>"{ATTEST_TEXT}"</p>
            <p style="color:var(--text-muted);font-size:0.8125rem">
              Recorded as an attestation on this test. You can still amend individual items afterwards.
            </p>
            <div class="form-actions">
              <button
                class="btn"
                onClick={() => {
                  onCheckAll(confirming);
                  setConfirming(null);
                }}
              >
                I confirm
              </button>
              <button class="btn btn--secondary" onClick={() => setConfirming(null)}>
                Cancel
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
