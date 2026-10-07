// Sends tests captured offline as soon as the connection is back, without the tester pressing
// anything (PRD story 34). Before this, a test completed on-farm stayed device-only until someone
// remembered Sync now — and a device is one dropped iPad away from losing it.
//
// When it tries: the browser says the network is back, the session check says we're online again
// (e.g. after signing back in), the tester returns to the app, and once when a page loads. Only
// when there is something to send, the device thinks it's online, and the session isn't known to
// have lapsed. A failed attempt backs off (30 s, 1, 2, 5, 10, then every 15 minutes); a successful
// one resets that. A lapsed session waits for the tester to sign in rather than retrying on a
// timer — nothing will succeed until they do, and the header already says "Signed out".
//
// Background Sync would be the textbook tool, but iPad Safari doesn't have it (plans §2), so this
// runs while the app is open — which is when testers have it in their hands anyway.
import { countUnsynced } from "../db/testStore";
import { currentConnection, onConnectionChange } from "../connectivity";
import { SessionExpiredError, syncAll, type SyncResult } from "./syncClient";

export const BACKOFF_MS = [30_000, 60_000, 120_000, 300_000, 600_000, 900_000];

export interface AutoSyncDeps {
  sync: () => Promise<SyncResult>;
  /** Tests on the device the server hasn't got. */
  unsynced: () => Promise<number>;
  /** Whether it's worth trying at all right now. */
  canTry: () => boolean;
  setTimer: (fn: () => void, ms: number) => unknown;
  clearTimer: (handle: unknown) => void;
}

export interface AutoSync {
  /** Try now, if there's anything to send and a chance of sending it. */
  trigger: (reason: string) => Promise<void>;
  /** The session is good again (signed back in): lift a sign-out pause and try. */
  sessionRestored: () => Promise<void>;
  /** Attempts that failed in a row; 0 after a success. */
  failures: () => number;
}

export function createAutoSync(deps: AutoSyncDeps): AutoSync {
  let busy = false;
  let again = false;
  let failures = 0;
  let pausedForSignIn = false;
  let retry: unknown = null;

  const scheduleRetry = () => {
    const delay = BACKOFF_MS[Math.min(failures, BACKOFF_MS.length - 1)];
    failures++;
    retry = deps.setTimer(() => {
      retry = null;
      void trigger("retry");
    }, delay);
  };

  const trigger = async (_reason: string): Promise<void> => {
    if (busy) {
      // Something happened mid-run (a test just saved, the network flickered): one more pass after.
      again = true;
      return;
    }
    if (pausedForSignIn || !deps.canTry()) return;
    if (retry !== null) {
      // An event beat the timer to it — this is a better moment than the timer's.
      deps.clearTimer(retry);
      retry = null;
    }
    busy = true;
    try {
      if ((await deps.unsynced()) === 0) {
        failures = 0;
        return;
      }
      const result = await deps.sync();
      if (result.failed > 0) scheduleRetry();
      else failures = 0;
    } catch (e) {
      if (e instanceof SessionExpiredError) pausedForSignIn = true;
      else scheduleRetry();
    } finally {
      busy = false;
      if (again) {
        again = false;
        void trigger("again");
      }
    }
  };

  return {
    trigger,
    sessionRestored: () => {
      pausedForSignIn = false;
      return trigger("session restored");
    },
    failures: () => failures,
  };
}

let started: AutoSync | null = null;

/** Starts automatic sync for this page (tester pages only, once). */
export function startAutoSync(): AutoSync {
  if (started) return started;
  const auto = createAutoSync({
    sync: syncAll,
    unsynced: countUnsynced,
    canTry: () => navigator.onLine !== false && currentConnection() !== "offline",
    setTimer: (fn, ms) => setTimeout(fn, ms),
    clearTimer: (handle) => clearTimeout(handle as ReturnType<typeof setTimeout>),
  });
  addEventListener("online", () => void auto.trigger("online"));
  document.addEventListener("visibilitychange", () => {
    if (document.visibilityState === "visible") void auto.trigger("visible");
  });
  let last = currentConnection();
  onConnectionChange((connection) => {
    if (connection === "online" && last !== "online") void auto.sessionRestored();
    last = connection;
  });
  started = auto;
  return auto;
}
