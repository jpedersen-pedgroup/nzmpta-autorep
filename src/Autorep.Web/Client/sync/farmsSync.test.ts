import "fake-indexeddb/auto";
import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { deleteReference, getReference, putReference, QUEUED_FARM_KEY_PREFIX, referencesWithPrefix } from "../db/testStore";
import {
  addFarmToCache,
  flushQueuedFarms,
  getCachedFarm,
  getCachedFarmOptions,
  getCachedFarms,
  initFarmOptions,
  initFarms,
  queuedFarms,
  queueFarm,
  warmMilkCompanyLogos,
  type CachedFarm,
  type NewFarmRequest,
} from "./farmsSync";

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
  await putReference({ key: "newFarmOptions", rows: undefined });
  for (const e of await referencesWithPrefix(QUEUED_FARM_KEY_PREFIX)) await deleteReference(e.key);
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

const request = (id: string, patch: Partial<NewFarmRequest> = {}): NewFarmRequest => ({
  id,
  name: "Totara Valley",
  supplyNumber: "70777",
  milkSupplyCompanyId: "m-1",
  regionId: "r-1",
  addressLine1: "12 Valley Road",
  addressLine2: null,
  town: "Te Kuiti",
  postCode: null,
  rapidNumber: null,
  farmerName: null,
  contactPhone: null,
  contactEmail: null,
  ...patch,
});

const OPTIONS = {
  regions: [{ id: "r-1", name: "King Country", island: "North Island" }],
  milkCompanies: [{ id: "m-1", name: "Fonterra" }],
};

describe("the add-a-farm form's pick lists", () => {
  it("keeps them on the device for the form to use with no signal", async () => {
    stubFetch(() => json(OPTIONS));
    await initFarmOptions();
    expect(calls[0].url).toBe("/api/farms/new-farm-options");

    stubFetch(() => {
      throw new TypeError("Failed to fetch");
    });
    await initFarmOptions();
    expect(await getCachedFarmOptions()).toEqual(OPTIONS);
  });

  it("is null until they've been downloaded, and a bad answer changes nothing", async () => {
    expect(await getCachedFarmOptions()).toBeNull();
    stubFetch(() => json({ regions: "nope" }));
    await initFarmOptions();
    expect(await getCachedFarmOptions()).toBeNull();
  });
});

describe("a farm added with no signal", () => {
  it("is offered straight away, with the names of what was picked for it, and marked as not sent", async () => {
    await putReference({ key: "farms", version: '"v1"', rows: [KOWHAI] });

    const shown = await queueFarm(request("f-new"), OPTIONS);

    expect(shown).toMatchObject({ id: "f-new", regionName: "King Country", milkCompanyName: "Fonterra", notSent: true });
    expect((await getCachedFarms()).map((f) => f.id)).toEqual(["f-1", "f-new"]);
    expect((await getCachedFarm("f-new"))?.town).toBe("Te Kuiti");
  });

  it("still queues when the device has no pick lists yet", async () => {
    const shown = await queueFarm(request("f-new", { milkSupplyCompanyId: null, regionId: null }), null);
    expect(shown).toMatchObject({ regionName: null, milkCompanyName: null });
  });

  it("isn't lost when a fresh book replaces the cached one", async () => {
    await queueFarm(request("f-new"), OPTIONS);
    stubFetch(() => json([KOWHAI], '"v2"'));

    await initFarms();

    expect((await getCachedFarms()).map((f) => f.id)).toEqual(["f-1", "f-new"]);
  });

  it("goes up under the id the device gave it, marked as queued, then is an ordinary book farm", async () => {
    await queueFarm(request("f-new"), OPTIONS);
    stubFetch(() => json({ id: "f-new", name: "Totara Valley", town: "Te Kuiti" }));

    expect(await flushQueuedFarms()).toBe(true);

    expect(calls[0].url).toBe("/api/farms");
    expect(calls[0].init?.method).toBe("POST");
    expect(JSON.parse(String(calls[0].init?.body))).toMatchObject({ id: "f-new", name: "Totara Valley", addressLine1: "12 Valley Road", queued: true });
    expect(await queuedFarms()).toEqual([]);
    expect((await getCachedFarms()).filter((f) => f.id === "f-new")).toEqual([{ id: "f-new", name: "Totara Valley", town: "Te Kuiti" }]);
  });

  it("stays queued when there's no connection, and that isn't an error", async () => {
    await queueFarm(request("f-new"), OPTIONS);
    stubFetch(() => {
      throw new TypeError("Failed to fetch");
    });

    expect(await flushQueuedFarms()).toBe(true);

    expect((await queuedFarms()).map((q) => q.farm.id)).toEqual(["f-new"]);
  });

  it("stays queued, and says so, when the session has lapsed", async () => {
    await queueFarm(request("f-new"), OPTIONS);
    stubFetch(() => new Response(null, { status: 401 }));

    expect(await flushQueuedFarms()).toBe(false);

    expect((await queuedFarms()).map((q) => q.farm.id)).toEqual(["f-new"]);
  });

  it("stays queued with the server's reason when it's refused, and is tried again next time", async () => {
    await queueFarm(request("f-new"), OPTIONS);
    stubFetch(() => new Response(JSON.stringify({ error: "Your account isn't linked to a Testing Company" }), { status: 400 }));

    await flushQueuedFarms();

    expect((await queuedFarms())[0].problem).toContain("Testing Company");

    stubFetch(() => json({ id: "f-new", name: "Totara Valley" }));
    await flushQueuedFarms();
    expect(await queuedFarms()).toEqual([]);
  });

  it("goes up oldest first", async () => {
    await queueFarm(request("f-a", { name: "First" }), OPTIONS);
    await new Promise((resolve) => setTimeout(resolve, 2));
    await queueFarm(request("f-b", { name: "Second" }), OPTIONS);

    expect((await queuedFarms()).map((q) => q.farm.name)).toEqual(["First", "Second"]);
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
