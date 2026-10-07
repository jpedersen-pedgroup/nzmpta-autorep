// The header's connection + unsynced-work indicator, on every tester page that loads the bundle
// and in the offline shell. Once pages can be served from the device, its absence would be
// actively misleading: the tester could no longer tell a live page from one drawn offline, or know
// that a day's work is still only on the iPad.
import { render } from "preact";
import { useEffect, useState } from "preact/hooks";
import { useConnection, type Connection } from "../connectivity";
import { countUnsynced, TESTS_CHANGED_EVENT } from "../db/testStore";
import { SYNC_STATE_EVENT } from "../sync/syncClient";

export function mountAppStatus(root: HTMLElement): void {
  render(<AppStatus />, root);
}

export interface StatusText {
  /** Short pill text. */
  label: string;
  /** The full sentence, for the tooltip and screen readers. */
  detail: string;
}

/** What the indicator says. `pending` is the count of tests the server hasn't got (null = unknown);
 * `syncing` is true while a sync is under way (started by hand or by itself). */
export function statusText(connection: Connection, pending: number | null, syncing = false): StatusText {
  const n = pending ?? 0;
  if (connection === "online" && syncing && n > 0) {
    return {
      label: `Online · sending ${n}…`,
      detail: `Connected. Sending ${n} test${n === 1 ? "" : "s"} from this device now.`,
    };
  }
  const tests = `${n} test${n === 1 ? "" : "s"}`;
  if (connection === "signed-out") {
    return {
      label: n > 0 ? `Signed out · ${n} unsent` : "Signed out",
      detail:
        "Your sign-in has expired. Sign in again to sync — " +
        (n > 0 ? `${tests} on this device are safe and will send once you do.` : "nothing on this device is waiting."),
    };
  }
  if (connection === "offline") {
    return {
      label: n > 0 ? `Offline · ${n} unsent` : "Offline",
      detail:
        n > 0
          ? `No connection. ${tests} ${n === 1 ? "is" : "are"} saved only on this device and will send when you're back online.`
          : "No connection. Keep working — tests save on this device.",
    };
  }
  return {
    label: n > 0 ? `Online · ${n} to send` : "Online",
    detail:
      n > 0
        ? `Connected. ${tests} on this device ${n === 1 ? "hasn't" : "haven't"} been sent yet.`
        : "Connected — everything on this device has been sent.",
  };
}

/** Live count of local-only tests: re-read whenever a test is written here or the tab returns. */
export function usePendingCount(): number | null {
  const [count, setCount] = useState<number | null>(null);
  useEffect(() => {
    let active = true;
    const read = () => {
      countUnsynced().then(
        (n) => active && setCount(n),
        () => undefined,
      );
    };
    const onVisible = () => {
      if (document.visibilityState === "visible") read();
    };
    read();
    addEventListener(TESTS_CHANGED_EVENT, read);
    document.addEventListener("visibilitychange", onVisible);
    return () => {
      active = false;
      removeEventListener(TESTS_CHANGED_EVENT, read);
      document.removeEventListener("visibilitychange", onVisible);
    };
  }, []);
  return count;
}

/** True while a sync is under way. */
function useSyncing(): boolean {
  const [syncing, setSyncing] = useState(false);
  useEffect(() => {
    const onState = (e: Event) => setSyncing(!!(e as CustomEvent<{ running: boolean }>).detail?.running);
    addEventListener(SYNC_STATE_EVENT, onState);
    return () => removeEventListener(SYNC_STATE_EVENT, onState);
  }, []);
  return syncing;
}

function AppStatus() {
  const connection = useConnection();
  const pending = usePendingCount();
  const syncing = useSyncing();
  const { label, detail } = statusText(connection, pending, syncing);
  // Signed out: the one state with an action the tester must take. Otherwise My tests, where the
  // work is listed and Sync now lives.
  const href =
    connection === "signed-out"
      ? `/Account/Login?ReturnUrl=${encodeURIComponent(location.pathname + location.search)}`
      : "/App/Tests";
  return (
    <a class={`app-status app-status--${connection}`} href={href} title={detail} aria-label={detail} data-connection={connection}>
      <span class="app-status__dot" aria-hidden="true"></span>
      <span>{label}</span>
    </a>
  );
}
