// Reusable visual-faults checklist step (pre-start and running). Each item is OK / Fault / blank;
// a fault captures severity + a note. "Check all as verified" raises an attestation modal.
import { useState } from "preact/hooks";
import type { FaultSeverity, VisualFaultEntry } from "./types";
import { type ChecklistSection, checklistComplete } from "./visualChecklist";

interface Props {
  title: string;
  sections: ChecklistSection[];
  entries: Record<string, VisualFaultEntry>;
  onSetEntry: (key: string, entry: VisualFaultEntry | null) => void;
  onCheckAll: () => void;
  attested: boolean;
  guards?: { value: boolean; onChange: (v: boolean) => void };
}

export function VisualFaultsStep({ title, sections, entries, onSetEntry, onCheckAll, attested, guards }: Props) {
  const [confirming, setConfirming] = useState(false);
  const complete = checklistComplete(sections, entries);

  return (
    <div class="card">
      <div class="card__title">{title}</div>
      <p style="color:var(--text-muted);margin-bottom:var(--space-4)">
        Mark each item OK, or log a fault. Items left blank are assumed not present.
      </p>

      {guards && (
        <label class="form-check" style="margin-bottom:var(--space-2)">
          <input
            type="checkbox"
            checked={guards.value}
            onChange={(e) => guards.onChange((e.currentTarget as HTMLInputElement).checked)}
          />
          <span>Guards installed on pulsators</span>
        </label>
      )}

      {sections.map((sec) => (
        <div key={sec.key}>
          <div class="section-title">{sec.title}</div>
          {sec.items.map((it) => {
            const entry = entries[it.key];
            return (
              <div key={it.key}>
                <div class="checkitem">
                  <div class="checkitem__name">{it.label}</div>
                  <div class="checkitem__actions">
                    <button
                      class={"ok" + (entry?.status === "ok" ? " on" : "")}
                      onClick={() => onSetEntry(it.key, entry?.status === "ok" ? null : { status: "ok" })}
                    >
                      OK
                    </button>
                    <button
                      class={"bad" + (entry?.status === "fault" ? " on" : "")}
                      onClick={() =>
                        onSetEntry(it.key, entry?.status === "fault" ? null : { status: "fault", severity: "Major" })
                      }
                    >
                      Fault
                    </button>
                  </div>
                </div>
                {entry?.status === "fault" && (
                  <div class="fault-detail">
                    <select
                      value={entry.severity ?? "Major"}
                      onChange={(e) =>
                        onSetEntry(it.key, { ...entry, severity: (e.currentTarget as HTMLSelectElement).value as FaultSeverity })
                      }
                    >
                      <option value="Critical">Critical</option>
                      <option value="Major">Major</option>
                      <option value="Minor">Minor</option>
                    </select>
                    <input
                      type="text"
                      placeholder="Note / observation"
                      value={entry.note ?? ""}
                      onInput={(e) => onSetEntry(it.key, { ...entry, note: (e.currentTarget as HTMLInputElement).value })}
                    />
                  </div>
                )}
              </div>
            );
          })}
        </div>
      ))}

      <button class="btn btn--add" style="margin-top:var(--space-4)" onClick={() => setConfirming(true)}>
        ✓ Check all as verified{attested ? " · attested" : ""}
      </button>
      {complete && (
        <p style="color:var(--success);font-size:0.85rem;margin-top:var(--space-2)">All items recorded.</p>
      )}

      {confirming && (
        <div
          class="modal-overlay open"
          onClick={(e) => {
            if (e.target === e.currentTarget) setConfirming(false);
          }}
        >
          <div class="modal">
            <div class="modal__title">Confirm: check all as verified</div>
            <p>"I have inspected all items on this page and confirm they have been seen, tested and are in order."</p>
            <p style="color:var(--text-muted);font-size:0.8125rem">
              Recorded as an attestation on this test. You can still amend individual items afterwards.
            </p>
            <div class="form-actions">
              <button
                class="btn"
                onClick={() => {
                  onCheckAll();
                  setConfirming(false);
                }}
              >
                I confirm
              </button>
              <button class="btn btn--secondary" onClick={() => setConfirming(false)}>
                Cancel
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
