// Real connectivity, in the three states the tester actually needs to tell apart:
//  - "online": the server answers AND this device's session is good — syncing will work.
//  - "signed-out": the server answers, but the session has lapsed (an 8-hour cookie is shorter
//    than a field day). Work is safe on the device; the fix is signing in, not waiting for signal.
//  - "offline": no network, a captive portal, a hung request, or a server that won't answer.
// navigator.onLine alone can't do this — it is true on a farm's Wi-Fi with no internet behind it.
//
// One shared monitor per page (not one poll per component): /health every 30 s for reachability,
// which never touches the session, plus GET /api/session — the authenticated check — on start-up,
// when the network comes back (with up to three quick re-checks if the server can't be confirmed
// straight away, see RECONNECT_RECHECKS_MS) and when the tab regains focus. The session check is
// deliberately NOT on the 30 s timer: it would renew the sliding sign-in cookie for ever on an idle
// open tab.
import { useEffect, useState } from "preact/hooks";
import { identityFromSession, saveIdentity } from "./db/identity";

const HEALTH_URL = "/health";
const SESSION_URL = "/api/session";
const POLL_MS = 30_000; // re-check reachability every 30s
export const TIMEOUT_MS = 5_000; // treat a slow/hung request as offline

export type Connection = "online" | "offline" | "signed-out";

/**
 * fetch with a deadline on the response headers. Weak rural signal and captive portals hang
 * rather than fail, and a request that never settles keeps a spinner up for the browser's default
 * timeout (minutes) — long after the tester has given up.
 */
export async function fetchWithTimeout(
  input: RequestInfo | URL,
  init: RequestInit = {},
  ms = TIMEOUT_MS,
): Promise<Response> {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), ms);
  try {
    return await fetch(input, { ...init, signal: controller.signal });
  } finally {
    clearTimeout(timer);
  }
}

const contentType = (res: Response) => res.headers.get("content-type") ?? "";

/** True when /health answers as the server — not a captive portal's sign-in page, which is a 200
 * HTML document (or a redirect to one). */
export async function isServerReachable(): Promise<boolean> {
  if (!navigator.onLine) return false; // definitely offline — skip the request
  try {
    const res = await fetchWithTimeout(HEALTH_URL, { method: "GET", cache: "no-store", redirect: "manual" });
    return res.ok && !contentType(res).includes("html");
  } catch {
    return false;
  }
}

// ---- the shared state ------------------------------------------------------------------------

let connection: Connection =
  typeof navigator !== "undefined" && navigator.onLine === false ? "offline" : "online";
/** The last authenticated answer was "not signed in". Sticks until a session check or an API call
 * succeeds, so a /health poll can't paper over it. */
let sessionLapsed = false;
let sessionChecks = false;
let monitoring = false;
const listeners = new Set<(c: Connection) => void>();

function set(next: Connection): void {
  if (next === connection) return;
  connection = next;
  for (const listener of listeners) listener(next);
}

export function currentConnection(): Connection {
  return connection;
}

export function onConnectionChange(listener: (c: Connection) => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

/** What an unauthenticated request just proved: the server is there, the session isn't. */
export function reportSignedOut(): void {
  sessionLapsed = true;
  set("signed-out");
}

/** An authenticated API call just succeeded. */
export function reportSessionOk(): void {
  sessionLapsed = false;
  set("online");
}

/** A reachability probe (or a request that failed for want of a network) just answered. */
export function reportReachable(ok: boolean): void {
  set(!ok ? "offline" : sessionLapsed ? "signed-out" : "online");
}

/**
 * GET /api/session: refreshes the device's identity record and settles all three states. Tester
 * pages only — an administrator's session gets a 403 here, which is not "signed out".
 */
export async function checkSession(): Promise<Connection> {
  if (typeof navigator !== "undefined" && navigator.onLine === false) {
    set("offline");
    return connection;
  }
  try {
    const res = await fetchWithTimeout(SESSION_URL, {
      headers: { Accept: "application/json" },
      cache: "no-store",
      redirect: "manual",
    });
    if (res.status === 401 || res.status === 403 || res.type === "opaqueredirect" || res.redirected) {
      reportSignedOut();
    } else if (!res.ok || !contentType(res).includes("json")) {
      // A 5xx, or a portal's HTML answering for the server: nothing will sync right now.
      set("offline");
    } else {
      const identity = identityFromSession(await res.json());
      if (identity) await saveIdentity(identity);
      reportSessionOk();
    }
  } catch {
    set("offline");
  }
  return connection;
}

// ---- quick re-checks after a reconnect ---------------------------------------------------------

/**
 * When the browser says the network is back but the first check can't confirm the server, check
 * again after these waits instead of leaving it to the 30 s poll (Josh, 13 Nov 2026). Signal on a
 * farm comes back weak: that first check often times out, and the tester would see "Offline", with
 * their work unsent, for up to half a minute after the bars returned. Three tries over about 20 s,
 * then the 30 s poll carries on as before. Bounded, because each one is a session check, which
 * renews the sign-in cookie (see the note at the top).
 */
export const RECONNECT_RECHECKS_MS = [3_000, 6_000, 12_000];

export interface ReconnectDeps {
  /** One check (the session check on tester pages, the reachability probe elsewhere). */
  recheck: () => Promise<Connection>;
  /** The current state, so a re-check that something else has already settled is skipped. */
  current: () => Connection;
  /** Whether the browser still thinks it has a network. */
  browserOnline: () => boolean;
  setTimer: (fn: () => void, ms: number) => unknown;
  clearTimer: (handle: unknown) => void;
}

export interface ReconnectChecks {
  /** The browser's "online" event: check now, and again shortly while the server can't be confirmed. */
  reconnected: () => Promise<void>;
  /** The browser's "offline" event: stop re-checking. */
  disconnected: () => void;
}

export function createReconnectChecks(deps: ReconnectDeps): ReconnectChecks {
  let pending: unknown = null;
  // Bumped by every reconnect and disconnect, so the answer to an older check can't schedule more.
  let run = 0;

  const cancel = () => {
    if (pending !== null) deps.clearTimer(pending);
    pending = null;
  };

  const attempt = async (thisRun: number, tries: number): Promise<void> => {
    const result = await deps.recheck();
    if (thisRun !== run || result !== "offline" || !deps.browserOnline()) return;
    if (tries >= RECONNECT_RECHECKS_MS.length) return; // the 30 s poll takes it from here
    pending = deps.setTimer(() => {
      pending = null;
      if (thisRun !== run || !deps.browserOnline() || deps.current() !== "offline") return;
      void attempt(thisRun, tries + 1);
    }, RECONNECT_RECHECKS_MS[tries]);
  };

  return {
    reconnected: () => {
      cancel();
      return attempt(++run, 0);
    },
    disconnected: () => {
      cancel();
      run++;
    },
  };
}

function startMonitoring(): void {
  if (monitoring || typeof window === "undefined") return;
  monitoring = true;
  const probe = () => void isServerReachable().then(reportReachable);
  const recheck = () => (sessionChecks ? void checkSession() : probe());
  const reconnect = createReconnectChecks({
    recheck: async () => {
      if (sessionChecks) return checkSession();
      reportReachable(await isServerReachable());
      return connection;
    },
    current: currentConnection,
    browserOnline: () => navigator.onLine !== false,
    setTimer: (fn, ms) => setTimeout(fn, ms),
    clearTimer: (handle) => clearTimeout(handle as ReturnType<typeof setTimeout>),
  });
  probe();
  setInterval(probe, POLL_MS);
  addEventListener("online", () => void reconnect.reconnected());
  addEventListener("offline", () => {
    reconnect.disconnected();
    set("offline");
  });
  document.addEventListener("visibilitychange", () => {
    if (document.visibilityState === "visible") recheck();
  });
}

/** Turns on the authenticated checks (tester pages) and runs one now. */
export function enableSessionChecks(): Promise<Connection> {
  sessionChecks = true;
  startMonitoring();
  return checkSession();
}

/** The live connection state; re-renders on every change. */
export function useConnection(): Connection {
  const [state, setState] = useState(connection);
  useEffect(() => {
    startMonitoring();
    setState(connection);
    return onConnectionChange(setState);
  }, []);
  return state;
}

/**
 * Tracks whether the server is reachable — signed in or not. Re-checks every 30s, on the browser's
 * online/offline events, and whenever the tab regains focus.
 */
export function useServerOnline(): boolean {
  return useConnection() !== "offline";
}
