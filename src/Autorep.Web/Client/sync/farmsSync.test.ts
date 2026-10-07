import "fake-indexeddb/auto";
import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { getReference, putReference } from "../db/testStore";
import { addFarmToCache, getCachedFarm, getCachedFarms, initFarms, warmMilkCompanyLogos, type CachedFarm } from "./farmsSync";

const KOWHAI: CachedFarm = { id: "f-1", name: "Kowhai Flats", supplyNumber: "40123", milkCompanyId: "m-1" };
const RIMU: CachedFarm = { id: "f-2", name: "Rimu Ridge", milkCompanyId: "m-2" };

const realFetch = globalThis.fetch;
let calls: { url: string; init?: RequestInit }[] = [];

function stubFetch(handler: (url: string, init?: RequestInit) => Response | Promise<Response>) {
  calls = [];
  globalThis.fetch = vi.fn(async (input: unknown, init?: RequestInit) => {
    calls.push({ url: String(input), init });
    return handler(String(input), init);
  }) as unknown as typeof fetch;
}

const json = (body: unknown, etag?: string) =>
  new Response(JSON.stringify(body), {
    status: 200,
    headers: { "content-type": "application/json", ...(etag ? { ETag: etag } : {}) },
  });

beforeEach(async () => {
  await putReference({ key: "farms", version: null, rows: undefined });
});

afterEach(() => {
  globalThis.fetch = realFetch;
});

describe("initFarms", () => {
  it("replaces the cached book in full on a fresh answer, and keeps its ETag", async () => {
    await putReference({ key: "farms", version: '"old"', rows: [{ id: "gone", name: "Deactivated since" }] });
    stubFetch(() => json([KOWHAI, RIMU], '"v2"'));

    expect(await initFarms()).toBe(true);

    expect((await getCachedFarms()).map((f) => f.id)).toEqual(["f-1", "f-2"]);
    expect((await getReference("farms"))?.version).toBe('"v2"');
  });

  it("asks conditionally with the ETag it holds, and a 304 leaves the book alone", async () => {
    await putReference({ key: "farms", version: '"v1"', rows: [KOWHAI] });
    stubFetch(() => new Response(null, { status: 304 }));

    expect(await initFarms()).toBe(false);

    expect(new Headers(calls[0].init?.headers).get("If-None-Match")).toBe('"v1"');
    expect(await getCachedFarms()).toEqual([KOWHAI]);
  });

  it("doesn't send an ETag for a book it doesn't actually hold", async () => {
    await putReference({ key: "farms", version: '"v1"', rows: undefined });
    stubFetch(() => json([KOWHAI], '"v2"'));

    await initFarms();

    expect(new Headers(calls[0].init?.headers).get("If-None-Match")).toBeNull();
  });

  it("leaves the cache untouched when the server says no (signed out, error)", async () => {
    await putReference({ key: "farms", version: '"v1"', rows: [KOWHAI] });
    stubFetch(() => new Response(null, { status: 401 }));

    expect(await initFarms()).toBe(false);
    expect(await getCachedFarms()).toEqual([KOWHAI]);
  });

  it("leaves the cache untouched when there's no network", async () => {
    await putReference({ key: "farms", version: '"v1"', rows: [KOWHAI] });
    stubFetch(() => {
      throw new TypeError("Failed to fetch");
    });

    expect(await initFarms()).toBe(false);
    expect(await getCachedFarms()).toEqual([KOWHAI]);
  });

  it("ignores an answer that isn't a farm list", async () => {
    await putReference({ key: "farms", version: '"v1"', rows: [KOWHAI] });
    stubFetch(() => json({ error: "not a list" }));

    expect(await initFarms()).toBe(false);
    expect(await getCachedFarms()).toEqual([KOWHAI]);
  });
});

describe("the cached book", () => {
  it("is empty on a device that has never synced", async () => {
    expect(await getCachedFarms()).toEqual([]);
    expect(await getCachedFarm("f-1")).toBeUndefined();
  });

  it("finds a farm by id, offline", async () => {
    await putReference({ key: "farms", version: null, rows: [KOWHAI, RIMU] });
    expect(await getCachedFarm("f-2")).toEqual(RIMU);
    expect(await getCachedFarm("nope")).toBeUndefined();
  });

  it("takes a farm the tester just added, in name order, without a second copy", async () => {
    await putReference({ key: "farms", version: '"v1"', rows: [RIMU] });

    await addFarmToCache(KOWHAI);
    await addFarmToCache({ ...KOWHAI, town: "Te Awamutu" });

    const rows = await getCachedFarms();
    expect(rows.map((f) => f.name)).toEqual(["Kowhai Flats", "Rimu Ridge"]);
    expect(rows[0].town).toBe("Te Awamutu");
  });
});

describe("warmMilkCompanyLogos", () => {
  const held = new Set<string>();
  beforeEach(() => {
    held.clear();
    (globalThis as { caches?: unknown }).caches = {
      match: async (url: string) => (held.has(url) ? new Response("logo") : undefined),
    };
  });
  afterEach(() => {
    delete (globalThis as { caches?: unknown }).caches;
  });

  it("fetches each company's logo once — the worker keeps it — and skips the ones already held", async () => {
    held.add("/api/milk-companies/m-2/logo");
    stubFetch(() => new Response("png", { status: 200 }));

    const fetched = await warmMilkCompanyLogos([KOWHAI, { ...RIMU }, { id: "f-3", name: "Also m-1", milkCompanyId: "m-1" }, { id: "f-4", name: "None" }]);

    expect(calls.map((c) => c.url)).toEqual(["/api/milk-companies/m-1/logo"]);
    expect(fetched).toBe(1);
  });

  it("stops quietly when the network goes", async () => {
    stubFetch(() => {
      throw new TypeError("Failed to fetch");
    });

    expect(await warmMilkCompanyLogos([KOWHAI, RIMU])).toBe(0);
    expect(calls).toHaveLength(1);
  });
});
