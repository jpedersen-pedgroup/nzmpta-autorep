import { describe, it, expect } from "vitest";
import {
  buildTestsQuery,
  formatCompleted,
  remainingCount,
  testerLabel,
  type CompanyTestRow,
} from "./companyTests";

function row(patch: Partial<CompanyTestRow> = {}): CompanyTestRow {
  return {
    id: "11111111-1111-1111-1111-111111111111",
    farmName: "Sunny Acres",
    testerName: "Wiremu Tester",
    completedAt: "2026-03-14T09:31:00.000Z",
    version: 1,
    isMine: false,
    ...patch,
  };
}

describe("buildTestsQuery", () => {
  it("omits empty filters so the URL stays readable", () => {
    expect(buildTestsQuery({})).toBe("");
    expect(buildTestsQuery({ q: "   ", skip: 0 })).toBe("");
  });

  it("trims and encodes the search term", () => {
    expect(buildTestsQuery({ q: "  Te Awa & Sons  " })).toBe("?q=Te+Awa+%26+Sons");
  });

  it("includes paging only when it means something", () => {
    expect(buildTestsQuery({ skip: 25, take: 25 })).toBe("?skip=25&take=25");
    expect(buildTestsQuery({ skip: 0, take: 25 })).toBe("?take=25");
  });
});

describe("testerLabel", () => {
  it("says 'You' for the viewer's own tests", () => {
    expect(testerLabel(row({ isMine: true }))).toBe("You");
  });

  it("uses the owner's display name", () => {
    expect(testerLabel(row())).toBe("Wiremu Tester");
  });

  // Never fall back to an email: testers don't recognise each other's logins, and it would
  // spread PII the list has no need to show.
  it("falls back to a neutral label when the name is missing", () => {
    expect(testerLabel(row({ testerName: null }))).toBe("A tester at your company");
    expect(testerLabel(row({ testerName: "  " }))).toBe("A tester at your company");
  });
});

describe("formatCompleted", () => {
  it("renders an unambiguous day/month/year", () => {
    const formatted = formatCompleted("2026-03-14T09:31:00.000Z");
    expect(formatted).toMatch(/14/);
    expect(formatted).toMatch(/Mar/);
    expect(formatted).toMatch(/2026/);
    expect(formatted).not.toMatch(/09:31/); // date only — the time is noise on a phone
  });

  it("passes an unparseable value straight through", () => {
    expect(formatCompleted("not-a-date")).toBe("not-a-date");
  });
});

describe("remainingCount", () => {
  it("counts what's left to load", () => {
    expect(remainingCount(100, 25)).toBe(75);
    expect(remainingCount(25, 25)).toBe(0);
  });

  it("never goes negative when the set shrank between requests", () => {
    expect(remainingCount(10, 25)).toBe(0);
  });
});
