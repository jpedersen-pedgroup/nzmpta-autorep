// Tells the server how many tests this device holds that the server hasn't got. A Super
// Administrator can't move a tester to another Testing Company while any of their devices last said
// it had some (Pages/Admin/Testers/Edit): a test first sent after a move is filed under the new
// company, so work done for the old one has to be sent, or deleted, first.
//
// The server only learns what a device says while it's online, so this reports whenever there's a
// chance: when the page has confirmed the session, when a sync finishes, when a test is saved or
// deleted, and when the connection comes back. Unchanged counts aren't re-sent for a while.
//
// The device id is random and kept in localStorage, so it survives reloads and sign-ins. It names a
// device, not a person. Each report also names whose store the count came from, and the server
// refuses it unless that's who is signed in: in the offline shell the store opened may belong to
// the device's last tester while the cookie is someone else's.
import { countUnsynced, storeOwner, TESTS_CHANGED_EVENT } from "../db/testStore";
import { currentConnection, fetchWithTimeout, onConnectionChange } from "../connectivity";
import { SYNC_STATE_EVENT } from "./syncClient";

const DEVICE_KEY = "autorep.deviceId";
/** How long an unchanged count goes without being sent again. */
export const RESEND_AFTER_MS = 10 * 60_000;
/** Saves come in bursts while a tester works through a step; one report covers the burst. */
export const SETTLE_MS = 2_000;

type KeyStore = Pick<Storage, "getItem" | "setItem">;

function localStore(): KeyStore | null {
  try {
    return globalThis.localStorage ?? null;
  } catch {
    return null;
  }
}

/** This device's id, made the first time it's asked for. Null when nothing can be kept (a blocked
 * or private store): a fresh id on every page would leave the server a trail of devices. */
export function deviceId(storage: KeyStore | null = localStore()): string | null {
  if (!storage) return null;
  try {
    const existing = storage.getItem(DEVICE_KEY);
    if (existing) return existing;
    const id = crypto.randomUUID();
    storage.setItem(DEVICE_KEY, id);
    return storage.getItem(DEVICE_KEY) === id ? id : null;
  } catch {
    return null;
  }
}

/** What the device calls itself for an administrator ("iPad · Safari"). iPadOS Safari says it's a
 * Mac, so a "Mac" with a touch screen is an iPad. */
export function deviceLabel(userAgent: string, touchPoints = 0): string {
  const ua = userAgent;
  const kind = /iPad/.test(ua) || (/Macintosh/.test(ua) && touchPoints > 1) ? "iPad"
    : /iPhone/.test(ua) ? "iPhone"
    : /Android/.test(ua) ? (/Mobile/.test(ua) ? "Android phone" : "Android tablet")
    : /Windows/.test(ua) ? "Windows PC"
    : /Macintosh/.test(ua) ? "Mac"
    : "Device";
  const browser = /Edg\//.test(ua) ? "Edge"
    : /(Chrome|CriOS)\//.test(ua) ? "Chrome"
    : /(Firefox|FxiOS)\//.test(ua) ? "Firefox"
    : /Safari\//.test(ua) ? "Safari"
    : null;
  return browser ? `${kind} · ${browser}` : kind;
}

export interface OutboxReport {
  deviceId: string;
  /** Whose store the count came from (the server refuses a report about anyone but the caller). */
  testerId: string;
  unsentTests: number;
  device: string;
}

export interface OutboxDeps {
  count: () => Promise<number>;
  /** Whose store the count reads, once it's open (testStore.storeOwner). */
  owner: () => string | null | undefined;
  online: () => boolean;
  send: (report: OutboxReport) => Promise<boolean>;
  deviceId: () => string | null;
  label: () => string;
  now: () => number;
}

const defaults: OutboxDeps = {
  label: () =>
    typeof navigator === "undefined" ? "Device" : deviceLabel(navigator.userAgent, navigator.maxTouchPoints ?? 0),
  count: countUnsynced,
  owner: storeOwner,
  online: () => currentConnection() === "online",
  send: async (report) => {
    try {
      const res = await fetchWithTimeout("/api/sync/outbox", {
        method: "POST",
        headers: { "Content-Type": "application/json", Accept: "application/json" },
        body: JSON.stringify(report),
      });
      return res.ok;
    } catch {
      return false;
    }
  },
  deviceId: () => deviceId(),
  now: () => Date.now(),
};

export function createOutboxReporter(deps: OutboxDeps = defaults) {
  let last: { owner: string; count: number; at: number } | null = null;

  /** Sends this device's count if it's online and the server hasn't heard it lately. */
  return async function report(): Promise<"sent" | "unchanged" | "skipped"> {
    if (!deps.online()) return "skipped";
    const id = deps.deviceId();
    if (!id) return "skipped";
    let count: number;
    try {
      count = await deps.count();
    } catch {
      return "skipped"; // the store isn't open (or has gone): nothing true to say
    }
    const owner = deps.owner();
    if (!owner) return "skipped"; // the legacy unnamed store belongs to nobody we can name
    if (last && last.owner === owner && last.count === count && deps.now() - last.at < RESEND_AFTER_MS) return "unchanged";
    if (!(await deps.send({ deviceId: id, testerId: owner, unsentTests: count, device: deps.label() }))) return "skipped";
    last = { owner, count, at: deps.now() };
    return "sent";
  };
}

/** Starts reporting for the tester whose store this page opened. Call once the session check has
 * confirmed who that is. */
export function startOutboxReports(): void {
  const report = createOutboxReporter();
  let settle: ReturnType<typeof setTimeout> | null = null;
  const soon = () => {
    if (settle) clearTimeout(settle);
    settle = setTimeout(() => {
      settle = null;
      void report();
    }, SETTLE_MS);
  };

  void report();
  addEventListener(SYNC_STATE_EVENT, (e) => {
    if (!(e as CustomEvent<{ running: boolean }>).detail?.running) void report();
  });
  addEventListener(TESTS_CHANGED_EVENT, soon);
  onConnectionChange((c) => {
    if (c === "online") void report();
  });
}
