import "fake-indexeddb/auto";
import { describe, it, expect, afterEach, vi } from "vitest";
import { cachedIdentity, clearIdentity } from "./db/identity";
import { checkSession, currentConnection, reportReachable, reportSessionOk } from "./connectivity";

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
    stubFetch(json({ testerId: "t-1", displayName: "One", userName: "one@test.local", roles: ["Tester"], syncOnly: false }));

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
