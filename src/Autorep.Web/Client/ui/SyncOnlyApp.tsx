// The whole tester app for a lapsed licence: show what is still only on this device, and send it.
// No wizard, no test list, no farm picker — those surfaces are closed to a sync-only session by
// policy, and this one exists purely so a day's captures aren't stranded when a licence runs out.
import { render } from "preact";
import { useEffect, useState } from "preact/hooks";
import { allTests, type LocalTest } from "../db/testStore";
import { syncAll, SessionExpiredError } from "../sync/syncClient";

export function mountSyncOnly(root: HTMLElement): void {
  render(<SyncOnlyApp />, root);
}

function SyncOnlyApp() {
  const [pending, setPending] = useState<LocalTest[] | null>(null);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ text: string; kind: "success" | "error" } | null>(null);

  const reload = async () =>
    setPending((await allTests()).filter((t) => t.syncState === "local-only"));

  useEffect(() => {
    void reload();
  }, []);

  const send = async () => {
    setBusy(true);
    setMessage(null);
    try {
      const r = await syncAll();
      await reload();
      if (r.failed > 0) {
        setMessage({
          text:
            `Sent ${r.pushed}. ${r.failed} couldn't be sent and ${r.failed === 1 ? "is" : "are"} ` +
            "still here — try again, and contact NZMPTA if it keeps failing.",
          kind: "error",
        });
      } else {
        setMessage({
          text: r.pushed > 0 ? `Sent ${r.pushed} test${r.pushed === 1 ? "" : "s"}. Nothing left on this device.` : "Nothing was waiting to send.",
          kind: "success",
        });
      }
    } catch (e) {
      setMessage({
        text:
          e instanceof SessionExpiredError
            ? "You've been signed out. Sign in again to send these — your tests are safe on this device."
            : "Couldn't reach the server. Your tests are safe on this device — try again when you have signal.",
        kind: "error",
      });
    } finally {
      setBusy(false);
    }
  };

  if (pending === null) return <p class="td-muted">Checking this device…</p>;

  const count = pending.length;
  return (
    <div>
      {count === 0 ? (
        <div class="alert alert--success">Nothing is waiting to send from this device.</div>
      ) : (
        <>
          <div class="alert alert--warning">
            <strong>
              {count} test{count === 1 ? "" : "s"} still only on this device.
            </strong>
            <ul style="margin:0.5rem 0 0;padding-left:1.25rem">
              {pending.map((t) => (
                <li key={t.id}>{t.farmName || "Unnamed farm"}</li>
              ))}
            </ul>
          </div>
          <button type="button" class="btn btn--full" disabled={busy} onClick={send}>
            {busy ? "Sending…" : `Send ${count} test${count === 1 ? "" : "s"}`}
          </button>
        </>
      )}
      {message && (
        <div class={`alert alert--${message.kind === "success" ? "success" : "danger"}`} style="margin-top:1rem">
          {message.text}
        </div>
      )}
    </div>
  );
}
