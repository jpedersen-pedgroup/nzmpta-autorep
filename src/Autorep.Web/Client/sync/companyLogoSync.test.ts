import "fake-indexeddb/auto";
import { describe, it, expect, afterEach, vi } from "vitest";
import { allTests, deleteTest, putReference, putTest, type LocalTest } from "../db/testStore";
import { defaultMachineConfiguration } from "../wizard/types";
import {
  bytesToDataUrl,
  isEmbeddableLogo,
  refreshCompanyLogo,
  refreshCompanyLogos,
  reportLogoFor,
  sniffImageType,
} from "./companyLogoSync";

// A real 1×1 PNG, and the first bytes of a JPEG / GIF — only the signatures matter here.
const PNG = Uint8Array.from(
  atob("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg=="),
  (c) => c.charCodeAt(0),
);
const PNG_2 = Uint8Array.from([...PNG, 0]); // "a replaced logo": different bytes, still a PNG
const JPEG = Uint8Array.from([0xff, 0xd8, 0xff, 0xe0, 0, 0x10, 0x4a, 0x46, 0x49, 0x46, 0, 1]);
const GIF = Uint8Array.from([0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 1, 0, 1, 0]);

function sample(id: string, patch: Partial<LocalTest> = {}): LocalTest {
  const now = "2026-09-23T00:00:00.000Z";
  return {
    id,
    farmName: "Sunny Acres",
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
    ...patch,
  };
}

const realFetch = globalThis.fetch;
type Handler = (url: string, init?: RequestInit) => Response | Promise<Response>;
function stubFetch(handler: Handler) {
  const fn = vi.fn(async (input: unknown, init?: RequestInit) => handler(String(input), init));
  globalThis.fetch = fn as unknown as typeof fetch;
  return fn;
}
const logoResponse = (bytes: Uint8Array, etag: string) =>
  new Response(new Blob([bytes as BlobPart]), { status: 200, headers: { "Content-Type": "image/png", ETag: etag } });
const json = (body: unknown) => new Response(JSON.stringify(body), { headers: { "content-type": "application/json" } });
const uid = () => crypto.randomUUID();

afterEach(async () => {
  globalThis.fetch = realFetch;
  for (const t of await allTests()) await deleteTest(t.id);
});

describe("image checks", () => {
  it("sniffs PNG and JPEG from the bytes and rejects anything else", () => {
    expect(sniffImageType(PNG)).toBe("image/png");
    expect(sniffImageType(JPEG)).toBe("image/jpeg");
    expect(sniffImageType(GIF)).toBeNull();
    expect(sniffImageType(new Uint8Array())).toBeNull();
  });

  it("only treats a data URL as embeddable when its bytes match its declared type", () => {
    expect(isEmbeddableLogo(bytesToDataUrl(PNG, "image/png"))).toBe(true);
    expect(isEmbeddableLogo(bytesToDataUrl(JPEG, "image/jpeg"))).toBe(true);
    // A JPEG labelled as a PNG, an SVG, garbage, and non-strings are all refused.
    expect(isEmbeddableLogo(bytesToDataUrl(JPEG, "image/png"))).toBe(false);
    expect(isEmbeddableLogo("data:image/svg+xml;base64,PHN2Zz48L3N2Zz4=")).toBe(false);
    expect(isEmbeddableLogo("data:image/png;base64,!!!!")).toBe(false);
    expect(isEmbeddableLogo(null)).toBe(false);
  });
});

describe("refreshCompanyLogo", () => {
  it("stores a downloaded logo as a data URL for offline use", async () => {
    const company = uid();
    stubFetch(() => logoResponse(PNG, '"v1"'));
    await refreshCompanyLogo(company);

    globalThis.fetch = (() => Promise.reject(new TypeError("offline"))) as typeof fetch;
    const logo = await reportLogoFor(sample("t", { testingCompanyId: company }));
    expect(logo).toBe(bytesToDataUrl(PNG, "image/png"));
  });

  it("revalidates with the cached ETag, keeps the logo on a 304 and replaces it on a change", async () => {
    const company = uid();
    stubFetch(() => logoResponse(PNG, '"v1"'));
    await refreshCompanyLogo(company);

    const unchanged = stubFetch(() => new Response(null, { status: 304 }));
    await refreshCompanyLogo(company);
    const sent = unchanged.mock.calls[0][1]?.headers as Record<string, string>;
    expect(sent["If-None-Match"]).toBe('"v1"');
    expect(await reportLogoFor(sample("t", { testingCompanyId: company }))).toBe(bytesToDataUrl(PNG, "image/png"));

    stubFetch(() => logoResponse(PNG_2, '"v2"'));
    await refreshCompanyLogo(company);
    expect(await reportLogoFor(sample("t", { testingCompanyId: company }))).toBe(bytesToDataUrl(PNG_2, "image/png"));
  });

  it("forgets the logo when the company has removed it (404)", async () => {
    const company = uid();
    stubFetch(() => logoResponse(PNG, '"v1"'));
    await refreshCompanyLogo(company);

    stubFetch(() => new Response(null, { status: 404 }));
    await refreshCompanyLogo(company);
    expect(await reportLogoFor(sample("t", { testingCompanyId: company }))).toBeNull();
  });

  it("keeps the cached logo when offline, signed out or the server errors", async () => {
    const company = uid();
    stubFetch(() => logoResponse(PNG, '"v1"'));
    await refreshCompanyLogo(company);

    for (const failure of [
      () => Promise.reject(new TypeError("offline")),
      () => Promise.resolve(new Response(null, { status: 401 })),
      () => Promise.resolve(new Response(null, { status: 500 })),
    ]) {
      globalThis.fetch = failure as unknown as typeof fetch;
      await refreshCompanyLogo(company);
      globalThis.fetch = (() => Promise.reject(new TypeError("offline"))) as typeof fetch;
      expect(await reportLogoFor(sample("t", { testingCompanyId: company }))).toBe(bytesToDataUrl(PNG, "image/png"));
    }
  });

  it("records a legacy logo the report can't print (e.g. GIF) as no logo", async () => {
    const company = uid();
    stubFetch(() => logoResponse(GIF, '"gif"'));
    await refreshCompanyLogo(company);
    globalThis.fetch = (() => Promise.reject(new TypeError("offline"))) as typeof fetch;
    expect(await reportLogoFor(sample("t", { testingCompanyId: company }))).toBeNull();
  });
});

describe("reportLogoFor — whose logo", () => {
  it("uses the tester's own company for a test the server hasn't stamped yet", async () => {
    const own = uid();
    stubFetch((url) =>
      url === "/api/profile/company" ? json({ id: own, name: "Own Co" }) : logoResponse(PNG, '"own"'),
    );
    await refreshCompanyLogos();

    globalThis.fetch = (() => Promise.reject(new TypeError("offline"))) as typeof fetch;
    expect(await reportLogoFor(sample("unsynced"))).toBe(bytesToDataUrl(PNG, "image/png"));
  });

  it("uses the company stamped on the test over the tester's current one", async () => {
    const own = uid();
    const previous = uid();
    await putTest(sample("old", { testingCompanyId: previous, syncState: "uploaded" }));
    stubFetch((url) => {
      if (url === "/api/profile/company") return json({ id: own, name: "New Co" });
      return url.includes(previous) ? logoResponse(JPEG, '"prev"') : logoResponse(PNG, '"own"');
    });
    // Pre-fetches both: the current company and the one an on-device test was done for.
    await refreshCompanyLogos();

    globalThis.fetch = (() => Promise.reject(new TypeError("offline"))) as typeof fetch;
    expect(await reportLogoFor(sample("old", { testingCompanyId: previous }))).toBe(bytesToDataUrl(JPEG, "image/jpeg"));
  });

  it("prints no logo when the server says the test has no company", async () => {
    await putReference({ key: "testerCompany", rows: { id: uid() } });
    const fetchSpy = stubFetch(() => logoResponse(PNG, '"x"'));
    expect(await reportLogoFor(sample("t", { testingCompanyId: null }))).toBeNull();
    expect(fetchSpy).not.toHaveBeenCalled();
  });

  it("fetches once on demand when nothing is cached yet, and never throws", async () => {
    const company = uid();
    stubFetch(() => logoResponse(PNG, '"v1"'));
    expect(await reportLogoFor(sample("t", { testingCompanyId: company }))).toBe(bytesToDataUrl(PNG, "image/png"));

    const other = uid();
    globalThis.fetch = (() => Promise.reject(new TypeError("offline"))) as typeof fetch;
    expect(await reportLogoFor(sample("t", { testingCompanyId: other }))).toBeNull();
  });
});
