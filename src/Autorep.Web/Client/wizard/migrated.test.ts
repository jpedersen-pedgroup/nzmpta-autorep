import { describe, expect, it } from "vitest";
import { isMigrated, migratedCutoffFrom, migratedHeadline } from "./migrated";

function memoryStorage(initial: Record<string, string> = {}) {
  const items = new Map(Object.entries(initial));
  return {
    items,
    getItem: (k: string) => items.get(k) ?? null,
    setItem: (k: string, v: string) => void items.set(k, v),
    removeItem: (k: string) => void items.delete(k),
  };
}

const page = (cutoff: string | null) => ({ getAttribute: (name: string) => (name === "data-migrated-cutoff" ? cutoff : null) });

describe("migratedHeadline", () => {
  it("names the cutoff date once it's set", () => {
    expect(migratedHeadline("2026-12-01")).toBe("Migrated from AutoRep Plus — tests before 1 December 2026");
  });

  it("leaves the date out while it's unset or unreadable", () => {
    expect(migratedHeadline(null)).toBe("Migrated from AutoRep Plus");
    expect(migratedHeadline("")).toBe("Migrated from AutoRep Plus");
    expect(migratedHeadline("2026-13-01")).toBe("Migrated from AutoRep Plus");
    expect(migratedHeadline("next month")).toBe("Migrated from AutoRep Plus");
  });
});

describe("migratedCutoffFrom", () => {
  it("takes the server page's date and remembers it for the offline shell", () => {
    const storage = memoryStorage();
    expect(migratedCutoffFrom(page("2026-12-01"), storage)).toBe("2026-12-01");
    // The offline shell document carries no attribute at all.
    expect(migratedCutoffFrom(page(null), storage)).toBe("2026-12-01");
  });

  it("forgets a remembered date when the server says it's unset", () => {
    const storage = memoryStorage({ "autorep.migratedCutoff": "2026-12-01" });
    expect(migratedCutoffFrom(page(""), storage)).toBeNull();
    expect(migratedCutoffFrom(page(null), storage)).toBeNull();
  });

  it("copes without storage", () => {
    expect(migratedCutoffFrom(page("2026-12-01"), null)).toBe("2026-12-01");
    expect(migratedCutoffFrom(page(null), null)).toBeNull();
  });
});

describe("isMigrated", () => {
  it("is a test that carries its recommendations as recorded", () => {
    expect(isMigrated({ recordedRecommendations: [] })).toBe(true);
    expect(isMigrated({})).toBe(false);
  });
});
