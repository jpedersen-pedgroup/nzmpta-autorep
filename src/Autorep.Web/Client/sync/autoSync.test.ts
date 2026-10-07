import { describe, it, expect, vi } from "vitest";
import { BACKOFF_MS, FOLLOW_UP_MS, createAutoSync, type AutoSyncDeps } from "./autoSync";
import { SessionExpiredError, StoreOwnerChangedError, type SyncResult } from "./syncClient";

const OK: SyncResult = { pushed: 1, failed: 0, pulled: 0 };

/** Deps with a manual timer queue: `timers` holds what was scheduled, `fire()` runs the next one. */
function harness(over: Partial<AutoSyncDeps> = {}) {
  const timers: { fn: () => void; ms: number; cleared: boolean }[] = [];
  const deps: AutoSyncDeps = {
    sync: vi.fn(async () => OK),
    unsynced: vi.fn(async () => 1),
    canTry: () => true,
    setTimer: (fn, ms) => {
      const t = { fn, ms, cleared: false };
      timers.push(t);
      return t;
    },
    clearTimer: (handle) => {
      (handle as { cleared: boolean }).cleared = true;
    },
    ...over,
  };
  const pending = () => timers.filter((t) => !t.cleared);
  const fire = async () => {
    const next = pending()[0];
    next.cleared = true;
    next.fn();
    await Promise.resolve();
    await new Promise((r) => setTimeout(r, 0));
  };
  return { deps, auto: createAutoSync(deps), timers, pending, fire };
}

describe("automatic sync", () => {
  it("sends when the connection is back and there's something to send", async () => {
    const { deps, auto } = harness();
    await auto.trigger("online");
    expect(deps.sync).toHaveBeenCalledTimes(1);
  });

  it("does nothing when everything is already on the server", async () => {
    const { deps, auto } = harness({ unsynced: vi.fn(async () => 0) });
    await auto.trigger("online");
    expect(deps.sync).not.toHaveBeenCalled();
  });

  it("with every test sent, still sends a report signed off with them — without waiting on it", async () => {
    let finish!: () => void;
    const flushReports = vi.fn(() => new Promise<void>((r) => (finish = r)));
    const { deps, auto } = harness({ unsynced: vi.fn(async () => 0), flushReports });

    await auto.trigger("online"); // returns while the report is still going

    expect(flushReports).toHaveBeenCalledTimes(1);
    expect(deps.sync).not.toHaveBeenCalled();
    // The next trigger isn't held up behind it either.
    await auto.trigger("visible");
    expect(flushReports).toHaveBeenCalledTimes(2);
    finish();
  });

  it("doesn't touch the report queue while the device is offline", async () => {
    const flushReports = vi.fn(async () => undefined);
    const { auto } = harness({ canTry: () => false, flushReports });
    await auto.trigger("visible");
    expect(flushReports).not.toHaveBeenCalled();
  });

  it("doesn't try while the device is offline", async () => {
    const { deps, auto } = harness({ canTry: () => false });
    await auto.trigger("visible");
    expect(deps.unsynced).not.toHaveBeenCalled();
    expect(deps.sync).not.toHaveBeenCalled();
  });

  it("backs off after each failure, and a success resets it", async () => {
    let fail = true;
    let sent = false;
    const { deps, auto, pending, fire } = harness({
      sync: vi.fn(async () => {
        if (fail) throw new TypeError("Failed to fetch");
        sent = true;
        return OK;
      }),
      unsynced: vi.fn(async () => (sent ? 0 : 1)),
    });

    await auto.trigger("online");
    expect(pending().map((t) => t.ms)).toEqual([BACKOFF_MS[0]]);
    await fire();
    expect(pending().map((t) => t.ms)).toEqual([BACKOFF_MS[1]]);
    expect(auto.failures()).toBe(2);

    fail = false;
    await fire();
    expect(deps.sync).toHaveBeenCalledTimes(3);
    expect(pending()).toHaveLength(0);
    expect(auto.failures()).toBe(0);
  });

  it("keeps backing off at the longest interval, never giving up", async () => {
    const { auto, pending, fire } = harness({ sync: vi.fn(async () => Promise.reject(new TypeError("down"))) });
    await auto.trigger("online");
    for (let i = 0; i < BACKOFF_MS.length + 2; i++) await fire();
    expect(pending().map((t) => t.ms)).toEqual([BACKOFF_MS[BACKOFF_MS.length - 1]]);
  });

  it("retries when the server took some tests but not all", async () => {
    const { auto, pending } = harness({ sync: vi.fn(async () => ({ pushed: 1, failed: 1, pulled: 0 })) });
    await auto.trigger("online");
    expect(pending()).toHaveLength(1);
  });

  it("waits for the tester to sign in again rather than retrying a lapsed session", async () => {
    const { deps, auto, pending } = harness({ sync: vi.fn(async () => Promise.reject(new SessionExpiredError())) });

    await auto.trigger("online");
    await auto.trigger("visible");
    expect(deps.sync).toHaveBeenCalledTimes(1);
    expect(pending()).toHaveLength(0);

    (deps.sync as ReturnType<typeof vi.fn>).mockImplementation(async () => OK);
    await auto.sessionRestored();
    expect(deps.sync).toHaveBeenCalledTimes(2);
  });

  it("runs once more when something happens mid-sync, instead of overlapping", async () => {
    let release!: () => void;
    const first = new Promise<SyncResult>((r) => (release = () => r(OK)));
    const sync = vi.fn().mockReturnValueOnce(first).mockResolvedValue(OK);
    const { auto } = harness({ sync });

    const running = auto.trigger("online");
    await new Promise((r) => setTimeout(r, 0));
    await auto.trigger("visible");
    await auto.trigger("online");
    expect(sync).toHaveBeenCalledTimes(1);

    release();
    await running;
    await new Promise((r) => setTimeout(r, 0));
    expect(sync).toHaveBeenCalledTimes(2);
  });

  // Codex review of #77: an edit made while a sync was on the wire stays local-only, and the sync
  // still reports success — so check again afterwards rather than wait for the next event.
  it("sends edits made during a successful sync shortly afterwards", async () => {
    const unsynced = vi
      .fn<() => Promise<number>>()
      .mockResolvedValueOnce(1) // before the sync: something to send
      .mockResolvedValueOnce(1) // after it: an edit landed meanwhile
      .mockResolvedValueOnce(1) // the follow-up: still there, send it
      .mockResolvedValue(0); // and now nothing
    const { deps, auto, pending, fire } = harness({ unsynced });

    await auto.trigger("online");
    expect(pending().map((t) => t.ms)).toEqual([FOLLOW_UP_MS]);
    expect(auto.failures()).toBe(0);

    await fire();
    expect(deps.sync).toHaveBeenCalledTimes(2);
    expect(pending()).toHaveLength(0);
  });

  it("stops when the signed-in tester has changed under the page — it is about to reload", async () => {
    const { deps, auto, pending } = harness({ sync: vi.fn(async () => Promise.reject(new StoreOwnerChangedError())) });

    await auto.trigger("online");
    await auto.trigger("visible");

    expect(deps.sync).toHaveBeenCalledTimes(1);
    expect(pending()).toHaveLength(0);
  });

  it("an event beats a pending retry: try now, not when the timer says", async () => {
    let fail = true;
    const { deps, auto, timers } = harness({
      sync: vi.fn(async () => {
        if (fail) throw new TypeError("down");
        return OK;
      }),
    });
    await auto.trigger("online");
    expect(timers[0].cleared).toBe(false);

    fail = false;
    await auto.trigger("online");
    expect(timers[0].cleared).toBe(true);
    expect(deps.sync).toHaveBeenCalledTimes(2);
  });
});
