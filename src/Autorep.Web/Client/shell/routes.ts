// The tester pages the offline shell can stand in for — the client-side twin of SHELL_ROUTES in
// wwwroot/sw.js. Keep the two lists the same: a route the worker serves the shell for but this
// file doesn't know renders "this page needs a connection" instead of the page.
//
// Pathname-based (the wizard always carries a query string) and case-insensitive, as ASP.NET
// routing is. Nothing under /Admin: the admin portal is online-only.
export type ShellRoute = "root" | "home" | "tests" | "new" | "wizard";

const ROUTES: Record<string, ShellRoute> = {
  "/": "root",
  "/app": "home",
  "/app/index": "home",
  "/app/tests": "tests",
  "/app/tests/index": "tests",
  "/app/tests/new": "new",
  "/app/tests/wizard": "wizard",
};

export function shellRouteFor(pathname: string): ShellRoute | null {
  const path = pathname.toLowerCase().replace(/\/+$/, "") || "/";
  return ROUTES[path] ?? null;
}

/** Every pathname the worker serves the shell for, normalised — exported for the parity test. */
export const SHELL_PATHS = Object.keys(ROUTES);

/** True on the tester app proper: the pages that keep the device's tester data fresh (farm book,
 * calibration, branding). Path-based, because in the shell the page's roots don't exist until the
 * bundle draws them. Excludes /Admin (a Super-Administrator's farm book is the national list) and
 * the lapsed-licence page. */
export function isTesterPath(pathname: string): boolean {
  return /^\/app(\/|$)/i.test(pathname);
}
