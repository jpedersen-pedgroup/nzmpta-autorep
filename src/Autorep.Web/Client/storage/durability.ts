// Keeping a tester's work on the device. On iPadOS a site's storage can be evicted when the device
// runs short or after a stretch without use, and an eviction takes IndexedDB with it — unsynced
// captures included. Three defences, none of which can be guaranteed by a web app, all cheap:
//  - ask the browser to keep this origin's storage (navigator.storage.persist), once there is real
//    work on the device worth keeping;
//  - show the tester how much space the app is using and whether it is protected;
//  - when a write is refused because the device is full, say so loudly instead of letting a
//    rejected promise swallow the edit.
// How iPadOS answers persist() for an installed web app is unverified on the target devices — see
// the real-device UAT case in plans/test-schedule.md.
import { STORAGE_FULL_EVENT } from "../db/testStore";

export interface StorageReport {
  /** Bytes this origin uses, when the browser says. */
  usage: number | null;
  /** Bytes it may use, when the browser says. */
  quota: number | null;
  /** True once the browser has agreed not to clear this origin's storage on its own. */
  persisted: boolean | null;
}

type StorageManagerLike = {
  estimate?: () => Promise<{ usage?: number; quota?: number }>;
  persisted?: () => Promise<boolean>;
  persist?: () => Promise<boolean>;
};

function storageManager(): StorageManagerLike | null {
  const nav = globalThis.navigator as { storage?: StorageManagerLike } | undefined;
  return nav?.storage ?? null;
}

export async function storageReport(): Promise<StorageReport> {
  const storage = storageManager();
  const [estimate, persisted] = await Promise.all([
    storage?.estimate?.().catch(() => null) ?? Promise.resolve(null),
    storage?.persisted?.().catch(() => null) ?? Promise.resolve(null),
  ]);
  return {
    usage: typeof estimate?.usage === "number" ? estimate.usage : null,
    quota: typeof estimate?.quota === "number" ? estimate.quota : null,
    persisted: typeof persisted === "boolean" ? persisted : null,
  };
}

let asked = false;

/** Asks once per page for persistent storage, when it isn't already granted. Returns whether the
 * origin's storage is now persistent (null when the browser can't say). Never throws. */
export async function requestPersistentStorage(): Promise<boolean | null> {
  const storage = storageManager();
  if (!storage?.persist) return null;
  try {
    if (await storage.persisted?.()) return true;
    if (asked) return false;
    asked = true;
    return await storage.persist();
  } catch {
    return null;
  }
}

/** "14.2 MB" — binary units, one decimal under 100. */
export function formatBytes(bytes: number): string {
  const units = ["bytes", "KB", "MB", "GB", "TB"];
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit++;
  }
  if (unit === 0) return `${Math.round(value)} bytes`;
  return `${value < 100 ? value.toFixed(1) : Math.round(value)} ${units[unit]}`;
}

/** One line for My tests: how much the app holds here, and whether it is protected. */
export function describeStorage(report: StorageReport): string | null {
  if (report.usage === null) return null;
  const used = `AutoRep is using ${formatBytes(report.usage)} on this device`;
  const of = report.quota ? ` of ${formatBytes(report.quota)} available` : "";
  const kept =
    report.persisted === true
      ? " — kept until you remove it."
      : report.persisted === false
        ? " — the device may clear it if space runs short, so sync often."
        : ".";
  return used + of + kept;
}

const FULL_ALERT_ID = "storage-full-alert";

/** Shows a persistent alert the first time a write is refused for want of space. */
export function watchForFullStorage(): void {
  if (typeof window === "undefined") return;
  addEventListener(STORAGE_FULL_EVENT, () => {
    if (document.getElementById(FULL_ALERT_ID)) return;
    const bar = document.createElement("div");
    bar.id = FULL_ALERT_ID;
    bar.className = "alert alert--danger storage-full";
    bar.setAttribute("role", "alert");
    bar.textContent =
      "This device is out of storage space, so your latest change could NOT be saved. Sync now to send " +
      "your tests, then free up space on the device (iPad: Settings › General › iPad Storage) before carrying on.";
    const main = document.querySelector("main");
    if (main) main.prepend(bar);
    else document.body.prepend(bar);
    bar.scrollIntoView({ block: "nearest" });
  });
}
