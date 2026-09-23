import { describe, it, expect, vi, afterEach } from "vitest";
import { GUIDES, guideHref, guidesForRoles, testerGuide, warmGuides } from "./guides";

// Who sees which work instructions. The server enforces the same roles on the files themselves
// (GuidesTests.cs); this is the client's reading of the shared Guides/guides.json.
describe("guide visibility by role", () => {
  const ids = (roles: string[]) => guidesForRoles(roles).map((g) => g.id);

  it("gives a Tester the tester guide only", () => {
    expect(ids(["Tester"])).toEqual(["tester"]);
  });

  it("gives a Company Administrator the tester and company admin guides", () => {
    expect(ids(["CompanyAdministrator"])).toEqual(["tester", "company-admin"]);
  });

  it("gives a Super Administrator all three", () => {
    expect(ids(["SuperAdministrator"])).toEqual(["tester", "company-admin", "super-admin"]);
  });

  it("gives an account with no known role nothing", () => {
    expect(ids([])).toEqual([]);
    expect(ids(["Farmer"])).toEqual([]);
  });

  it("keeps every file name URL-safe and every entry complete", () => {
    for (const g of GUIDES) {
      expect(g.file).toMatch(/^[a-z0-9-]+\.pdf$/);
      expect(g.date).toMatch(/^\d{4}-\d{2}-\d{2}$/);
      expect(g.title && g.description && g.version).toBeTruthy();
    }
  });

  it("links the tester app straight to the tester PDF, unversioned", () => {
    // The service worker keys its offline copy on the path, so the in-app link must not carry ?v=.
    expect(guideHref(testerGuide()!)).toBe("/guides/autorep-tester-guide.pdf");
  });
});

describe("warming guides for offline use", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    Reflect.deleteProperty(globalThis.navigator as object, "connection");
    Reflect.deleteProperty(globalThis.navigator as object, "serviceWorker");
  });

  const withWorker = () =>
    Object.defineProperty(globalThis.navigator, "serviceWorker", { value: { controller: {} }, configurable: true });

  const pdf = () => new Response("%PDF-1.7", { status: 200, headers: { "Content-Type": "application/pdf" } });

  it("fetches each guide when a service worker is there to keep it", async () => {
    withWorker();
    const fetchMock = vi.fn(async () => pdf());
    vi.stubGlobal("fetch", fetchMock);

    await expect(warmGuides(guidesForRoles(["Tester"]))).resolves.toBe(true);
    expect(fetchMock).toHaveBeenCalledWith("/guides/autorep-tester-guide.pdf", { credentials: "same-origin" });
  });

  it("doesn't download anything without a controlling service worker", async () => {
    const fetchMock = vi.fn(async () => pdf());
    vi.stubGlobal("fetch", fetchMock);

    await expect(warmGuides(guidesForRoles(["Tester"]))).resolves.toBe(false);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("skips the pre-emptive download when the browser asks to save data", async () => {
    withWorker();
    Object.defineProperty(globalThis.navigator, "connection", { value: { saveData: true }, configurable: true });
    const fetchMock = vi.fn(async () => pdf());
    vi.stubGlobal("fetch", fetchMock);

    await expect(warmGuides(guidesForRoles(["Tester"]))).resolves.toBe(false);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  // A signed-out fetch follows the redirect to the login page: a 200 that isn't the guide.
  it("doesn't count a login page as a kept guide", async () => {
    withWorker();
    vi.stubGlobal("fetch", vi.fn(async () => new Response("<html>", { headers: { "Content-Type": "text/html" } })));

    await expect(warmGuides(guidesForRoles(["Tester"]))).resolves.toBe(false);
  });

  // It runs off the back of a sync; throwing would take the sync's result down with it.
  it("reports failure rather than throwing when offline", async () => {
    withWorker();
    vi.stubGlobal("fetch", vi.fn(async () => Promise.reject(new TypeError("Failed to fetch"))));

    await expect(warmGuides(guidesForRoles(["Tester"]))).resolves.toBe(false);
  });
});
