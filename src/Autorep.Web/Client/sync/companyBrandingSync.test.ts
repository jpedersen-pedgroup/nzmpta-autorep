import "fake-indexeddb/auto";
import { describe, it, expect, beforeEach, vi, afterEach } from "vitest";
import { getCachedCompanyBranding, initCompanyBranding } from "./companyBrandingSync";
import { putReference } from "../db/testStore";

/** One canned response per call, in order; records the If-None-Match each call sent. */
function mockFetch(responses: Array<() => unknown>) {
  const sent: Array<string | undefined> = [];
  let i = 0;
  vi.stubGlobal(
    "fetch",
    vi.fn(async (_url: string, init?: RequestInit) => {
      sent.push((init?.headers as Record<string, string> | undefined)?.["If-None-Match"]);
      const r = responses[Math.min(i++, responses.length - 1)]();
      if (r instanceof Error) throw r;
      return r;
    }),
  );
  return sent;
}

const company = { id: "c1", name: "Sample Testing Co", logo: "data:image/png;base64,AQID" };
const ok = (body: unknown, etag = '"v1"') => ({
  ok: true, status: 200, headers: new Headers({ ETag: etag }), json: async () => body,
});
const status = (code: number) => ({ ok: code < 300, status: code, headers: new Headers(), json: async () => ({}) });

beforeEach(async () => {
  await putReference({ key: "testerCompany", rows: undefined });
});
afterEach(() => vi.unstubAllGlobals());

describe("company branding sync", () => {
  it("caches the tester's company and logo for offline reports", async () => {
    mockFetch([() => ok(company)]);
    await initCompanyBranding();
    expect(await getCachedCompanyBranding()).toEqual(company);
  });

  it("sends the cached ETag and keeps the cache on a 304, so an unchanged logo isn't re-downloaded", async () => {
    const sent = mockFetch([() => ok(company, '"v1"'), () => status(304)]);
    await initCompanyBranding();
    await initCompanyBranding();
    expect(sent).toEqual([undefined, '"v1"']);
    expect(await getCachedCompanyBranding()).toEqual(company);
  });

  it("replaces the cache when the logo changes", async () => {
    mockFetch([() => ok(company, '"v1"'), () => ok({ ...company, logo: "data:image/png;base64,CQkJ" }, '"v2"')]);
    await initCompanyBranding();
    await initCompanyBranding();
    expect((await getCachedCompanyBranding())?.logo).toBe("data:image/png;base64,CQkJ");
  });

  it("drops the cached company when the tester no longer has one", async () => {
    mockFetch([() => ok(company), () => status(204)]);
    await initCompanyBranding();
    await initCompanyBranding();
    expect(await getCachedCompanyBranding()).toBeNull();
  });

  it("keeps the last-known branding when offline or the server errors", async () => {
    mockFetch([() => ok(company), () => new Error("Failed to fetch"), () => status(500)]);
    await initCompanyBranding();
    await initCompanyBranding();
    await initCompanyBranding();
    expect(await getCachedCompanyBranding()).toEqual(company);
  });

  it("ignores a logo that isn't a data URL", async () => {
    mockFetch([() => ok({ ...company, logo: "https://example.invalid/logo.png" })]);
    await initCompanyBranding();
    expect((await getCachedCompanyBranding())?.logo).toBeNull();
  });
});
