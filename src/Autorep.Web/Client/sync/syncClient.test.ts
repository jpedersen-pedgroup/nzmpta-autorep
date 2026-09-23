import "fake-indexeddb/auto";
import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { allTests, deleteTest, getTest, putTest, type LocalTest } from "../db/testStore";
import { syncAll, SessionExpiredError } from "./syncClient";
import { defaultMachineConfiguration } from "../wizard/types";

function sample(id: string, farmName = "Sunny Acres"): LocalTest {
  const now = "2026-07-22T00:00:00.000Z";
  return {
    id,
    farmName,
    config: defaultMachineConfiguration(),
    currentStep: "Setup",
    visualFaults: {},
    attestations: [],
    readings: {},
    recommendations: {},
    dataFields: {},
    createdAt: now,
    updatedAt: now,
    markedCompleteAt: null,
    syncState: "local-only",
  };
}

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json" },
  });
}

/** An empty delta pull, so syncAll's pull leg always has something valid to consume. */
const EMPTY_PULL = { watermark: "2026-07-22T00:00:00.000Z", tests: [] };

const realFetch = globalThis.fetch;

function stubFetch(handler: (url: string, init?: RequestInit) => Response | Promise<Response>) {
  globalThis.fetch = vi.fn(async (input: unknown, init?: RequestInit) =>
    handler(String(input), init),
  ) as unknown as typeof fetch;
}

beforeEach(async () => {
  for (const t of await allTests()) await deleteTest(t.id);
});

afterEach(() => {
  globalThis.fetch = realFetch;
});

describe("syncAll", () => {
  // One test the server won't accept must not block the tests queued behind it, nor the pull.
  // Before this, pushTest threw straight out of the loop and syncing stayed wedged forever.
  it("keeps going when one push fails, and still pulls", async () => {
    await putTest(sample("bad", "Rejected Farm"));
    await putTest(sample("good", "Accepted Farm"));

    stubFetch((url, init) => {
      if (init?.method === "POST") {
        const body = String(init.body);
        return body.includes("Rejected Farm")
          ? jsonResponse({ error: "too big" }, 400)
          : jsonResponse({ id: "server-id", status: "created" }, 201);
      }
      return jsonResponse(EMPTY_PULL);
    });

    const result = await syncAll();

    expect(result).toEqual({ pushed: 1, failed: 1, pulled: 0 });
    expect((await getTest("good"))?.syncState).toBe("uploaded");
    expect((await getTest("bad"))?.syncState).toBe("local-only");
  });

  // The regression that silently lost a day's work: an expired cookie redirected /api to the
  // login page, fetch followed it (POST -> GET), and the resulting 200 HTML page read as ok —
  // so the test was marked uploaded although the server never received it, after which it could
  // be neither re-pushed nor deleted.
  it("treats a followed auth redirect as a failure, not a successful push", async () => {
    await putTest(sample("redirected"));

    stubFetch((_url, init) => {
      if (init?.method === "POST") {
        return {
          ok: true,
          status: 200,
          redirected: true,
          type: "basic",
          json: async () => ({}),
        } as unknown as Response;
      }
      return jsonResponse(EMPTY_PULL);
    });

    await expect(syncAll()).rejects.toBeInstanceOf(SessionExpiredError);

    const after = await getTest("redirected");
    expect(after?.syncState).toBe("local-only");
    expect(after?.everUploaded).toBeFalsy();
  });

  it("surfaces a 401 as an expired session and leaves the test unsent", async () => {
    await putTest(sample("unauthorised"));

    stubFetch((_url, init) =>
      init?.method === "POST" ? jsonResponse({}, 401) : jsonResponse(EMPTY_PULL),
    );

    await expect(syncAll()).rejects.toBeInstanceOf(SessionExpiredError);

    const after = await getTest("unauthorised");
    expect(after?.syncState).toBe("local-only");
    expect(after?.everUploaded).toBeFalsy();
  });

  it("reports a clean run when every push succeeds", async () => {
    await putTest(sample("ok1"));
    await putTest(sample("ok2"));

    stubFetch((_url, init) =>
      init?.method === "POST"
        ? jsonResponse({ id: "x", status: "created" }, 201)
        : jsonResponse(EMPTY_PULL),
    );

    const result = await syncAll();

    expect(result).toEqual({ pushed: 2, failed: 0, pulled: 0 });
  });
});
