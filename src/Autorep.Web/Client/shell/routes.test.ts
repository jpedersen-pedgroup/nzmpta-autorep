import { describe, it, expect } from "vitest";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { SHELL_PATHS, isTesterPath, shellRouteFor } from "./routes";

describe("shellRouteFor", () => {
  it("maps every tester page the shell stands in for", () => {
    expect(shellRouteFor("/")).toBe("root");
    expect(shellRouteFor("/App")).toBe("home");
    expect(shellRouteFor("/App/Index")).toBe("home");
    expect(shellRouteFor("/App/Tests")).toBe("tests");
    expect(shellRouteFor("/App/Tests/Index")).toBe("tests");
    expect(shellRouteFor("/App/Tests/New")).toBe("new");
    expect(shellRouteFor("/App/Tests/Wizard")).toBe("wizard");
  });

  it("matches the way ASP.NET routes: any case, trailing slash or not", () => {
    expect(shellRouteFor("/app/tests/wizard/")).toBe("wizard");
    expect(shellRouteFor("/APP")).toBe("home");
  });

  it("knows nothing about the admin portal or the account pages", () => {
    expect(shellRouteFor("/Admin")).toBeNull();
    expect(shellRouteFor("/Admin/Tests/View/1")).toBeNull();
    expect(shellRouteFor("/Account/Manage")).toBeNull();
    expect(shellRouteFor("/App/Tests/Company")).toBeNull();
  });

  // The worker decides WHEN the shell answers; this file decides WHAT it draws. A route in one list
  // but not the other is a page that either never works offline or draws "needs a connection".
  it("covers exactly the routes sw.js serves the shell for", () => {
    const sw = readFileSync(resolve(__dirname, "../../wwwroot/sw.js"), "utf8");
    const block = /const SHELL_ROUTES = \[([^\]]*)\];/.exec(sw);
    expect(block, "SHELL_ROUTES not found in sw.js").not.toBeNull();
    const swRoutes = [...block![1].matchAll(/'([^']+)'/g)].map((m) => m[1]).sort();
    expect(swRoutes).toEqual([...SHELL_PATHS].sort());
  });
});

describe("isTesterPath", () => {
  it("is the tester app proper", () => {
    expect(isTesterPath("/App")).toBe(true);
    expect(isTesterPath("/App/Tests/Wizard")).toBe(true);
    expect(isTesterPath("/app/tests/company")).toBe(true);
  });
  it("is not the admin portal, the lapsed-licence page, or a path that merely starts with App", () => {
    expect(isTesterPath("/Admin/Tests/View/1")).toBe(false);
    expect(isTesterPath("/Account/FinishSync")).toBe(false);
    expect(isTesterPath("/Apple")).toBe(false);
    expect(isTesterPath("/")).toBe(false);
  });
});
