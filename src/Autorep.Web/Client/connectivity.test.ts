import "fake-indexeddb/auto";
import { describe, it, expect, afterEach, vi } from "vitest";
import { cachedIdentity, clearIdentity } from "./db/identity";
import {
  checkSession,
  createReconnectChecks,
  currentConnection,
  RECONNECT_RECHECKS_MS,
  reportReachable,
  reportSessionOk,
  type Connection,
  type ReconnectDeps,
} from "./connectivity";

const realFetch = globalThis.fetch;

function stubFetch(response: Response | Error) {
  globalThis.fetch = vi.fn(async () => {
    if (response instanceof Error) throw response;
    return response;
  }) as unknown as typeof fetch;
}

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json; charset=utf-8" } });

afterEach(async () => {
  globalThis.fetch = realFetch;
  reportSessionOk(); // back to a clean "online" for the next case
  await clearIdentity();
});

describe("checkSession — GET /api/session", () => {
  it("is online and refreshes the device's identity record on a good answer", async () => {
    stubFetch(json({ testerId: "t-1", displayName: "One", userName: "one@test.local", roles: ["Tester"] }));

    expect(await checkSession()).toBe("online");
    expect(cachedIdentity()?.testerId).toBe("t-1");
  });

  it("reads a 401 as signed out — and a reachability poll can't paper over it", async () => {
    stubFetch(new Response(null, { status: 401 }));

    expect(await checkSession()).toBe("signed-out");
    reportReachable(true);
    expect(currentConnection()).toBe("signed-out");
    reportReachable(false);
    expect(currentConnection()).toBe("offline");
    reportReachable(true);
    expect(currentConnection()).toBe("signed-out");
  });

  it("treats a captive portal's HTML page as offline, not as a session", async () => {
    stubFetch(new Response("<html>Sign in to Farm Wi-Fi</html>", { status: 200, headers: { "content-type": "text/html" } }));

    expect(await checkSession()).toBe("offline");
    expect(cachedIdentity()).toBeNull();
  });

  it("is offline when the request fails or the server errors", async () => {
    stubFetch(new TypeError("Failed to fetch"));
    expect(await checkSession()).toBe("offline");

    stubFetch(json({ error: "boom" }, 503));
    expect(await checkSession()).toBe("offline");
  });

  it("never clears the identity record on a lapsed session — the tester keeps working offline", async () => {
    stubFetch(json({ testerId: "t-2", displayName: "Two", roles: ["Tester"] }));
    await checkSession();
    stubFetch(new Response(null, { status: 401 }));
    await checkSession();

    expect(cachedIdentity()?.testerId).toBe("t-2");
  });
});

/**
 * createReconnectChecks with a manual timer queue. Each check answers with the next of `answers`
 * ("offline" once they run out) and that answer becomes the current state, as checkSession's does.
 */
function reconnectHarness(answers: Connection[] = []) {
  const timers: { fn: () => void; ms: number; cleared: boolean }[] = [];
  let state: Connection = "offline";
  let browserOnline = true;
  const queue = [...answers];
  const recheck = vi.fn(async () => (state = queue.shift() ?? "offline"));
  const deps: ReconnectDeps = {
    recheck,
    current: () => state,
    browserOnline: () => browserOnline,
    setTimer: (fn, ms) => {
      const t = { fn, ms, cleared: false };
      timers.push(t);
      return t;
    },
    clearTimer: (handle) => {
      (handle as { cleared: boolean }).cleared = true;
    },
  };
  const pending = () => timers.filter((t) => !t.cleared);
  const settle = () => new Promise((r) => setTimeout(r, 0));
  const fire = async () => {
    const next = pending()[0];
    next.cleared = true;
    next.fn();
    await settle();
  };
  return {
    deps,
    checks: createReconnectChecks(deps),
    recheck,
    timers,
    pending,
    fire,
    settle,
    setState: (c: Connection) => (state = c),
    browserGoesOffline: () => (browserOnline = false),
  };
}

describe("quick re-checks after a reconnect", () => {
  it("checks at once, then at 3, 6 and 12 s while the server can't be confirmed, then leaves it to the poll", async () => {
    const h = reconnectHarness();

    await h.checks.reconnected();
    expect(h.recheck).toHaveBeenCalledTimes(1);
    for (let i = 0; i < RECONNECT_RECHECKS_MS.length; i++) await h.fire();

    expect(h.recheck).toHaveBeenCalledTimes(4);
    expect(h.timers.map((t) => t.ms)).toEqual([3_000, 6_000, 12_000]);
    expect(h.pending()).toHaveLength(0);
  });

  it("stops as soon as a check gets through", async () => {
    const h = reconnectHarness(["offline", "online"]);

    await h.checks.reconnected();
    await h.fire();

    expect(h.recheck).toHaveBeenCalledTimes(2);
    expect(h.pending()).toHaveLength(0);
  });

  it("takes a lapsed session as an answer: signing in is the fix, not checking again", async () => {
    const h = reconnectHarness(["signed-out"]);

    await h.checks.reconnected();

    expect(h.pending()).toHaveLength(0);
  });

  it("stops when the network drops again", async () => {
    const h = reconnectHarness();
    await h.checks.reconnected();

    h.checks.disconnected();

    expect(h.pending()).toHaveLength(0);
  });

  it("skips a re-check something else has already settled (the 30 s poll got through first)", async () => {
    const h = reconnectHarness();
    await h.checks.reconnected();

    h.setState("online");
    await h.fire();

    expect(h.recheck).toHaveBeenCalledTimes(1);
    expect(h.pending()).toHaveLength(0);
  });

  it("doesn't check while the browser says there's no network", async () => {
    const h = reconnectHarness();
    await h.checks.reconnected();

    h.browserGoesOffline();
    await h.fire();

    expect(h.recheck).toHaveBeenCalledTimes(1);
    expect(h.pending()).toHaveLength(0);
  });

  it("starts again from the first wait on another reconnect, rather than doubling up", async () => {
    const h = reconnectHarness();
    await h.checks.reconnected();
    await h.checks.reconnected();

    expect(h.recheck).toHaveBeenCalledTimes(2);
    expect(h.pending().map((t) => t.ms)).toEqual([3_000]);
  });

  it("schedules nothing from a check that was still out when the network dropped", async () => {
    const h = reconnectHarness();
    let answer!: (c: Connection) => void;
    h.recheck.mockImplementationOnce(() => new Promise<Connection>((r) => (answer = r)));

    const first = h.checks.reconnected();
    h.checks.disconnected();
    answer("offline");
    await first;
    await h.settle();

    expect(h.pending()).toHaveLength(0);
  });
});
