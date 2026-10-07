// The offline shell (wwwroot/app-shell.html): a static, identity-free document the service worker
// serves for a tester page when the server can't answer. Everything a server-rendered page would
// have carried — who is signed in, their licence banner, and the page itself — is drawn here from
// the device: the identity record (db/identity.ts) and the per-tester IndexedDB store.
//
// There is no router. Each tester page is a full document load that mounts its own independent
// Preact tree (main.ts mountApps), exactly as on a server-rendered page — the shell only has to put
// the right empty root in place first.
import { render, type ComponentChildren, type JSX } from "preact";
import { homeFor, type IdentityRecord } from "../db/identity";
import { licenceBanner } from "./licenceBanner";
import { shellRouteFor, type ShellRoute } from "./routes";

export function isShellDocument(): boolean {
  return document.body?.hasAttribute("data-app-shell") ?? false;
}

/** Fills the header's user block and the licence banner. The server-rendered header gets these
 * from Razor (Pages/Shared/_Layout.cshtml) — same initials rule, same banner thresholds. */
export function renderShellChrome(identity: IdentityRecord | null): void {
  const user = document.getElementById("shell-user");
  if (identity && user) {
    const name = identity.userName ?? identity.displayName;
    const initials = document.getElementById("shell-user-initials");
    const nameEl = document.getElementById("shell-user-name");
    if (initials) initials.textContent = name.slice(0, 1).toUpperCase();
    if (nameEl) nameEl.textContent = name;
    user.hidden = false;
  }

  const bannerRoot = document.getElementById("licence-banner-root");
  const banner = identity && homeFor(identity) !== "admin" ? licenceBanner(identity.licenceExpiryDate) : null;
  if (bannerRoot && banner) {
    render(
      <div class={`licence-banner${banner.urgent ? " licence-banner--urgent" : ""}`}>
        {banner.lead && <strong>{banner.lead}</strong>} {banner.rest}
      </div>,
      bannerRoot,
    );
  }
}

const TITLES: Record<ShellRoute, string> = {
  root: "AutoRep",
  home: "Home",
  tests: "My tests",
  new: "New test",
  wizard: "Machine test",
};

function Notice({ title, children, retry = true }: { title: string; children: ComponentChildren; retry?: boolean }) {
  return (
    <div class="card empty" style="max-width:640px;margin:0 auto">
      <h2>{title}</h2>
      {children}
      <div class="form-actions" style="justify-content:center">
        {retry && (
          <button type="button" class="btn" onClick={() => location.reload()}>
            Try again
          </button>
        )}
        <a class="btn btn--secondary" href="/App/Tests">
          My tests
        </a>
      </div>
    </div>
  );
}

function root(id: string): HTMLElement {
  const el = document.createElement("div");
  el.id = id;
  return el;
}

/**
 * Puts the page for this URL into the shell's <main>: an empty root that main.ts mounts as it
 * would on the server-rendered page, or a notice when there is nothing this device can show.
 * Returns the route drawn, or null when it drew a notice instead.
 */
export function renderShellPage(identity: IdentityRecord | null): ShellRoute | null {
  const main = document.getElementById("shell-main");
  if (!main) return null;
  main.replaceChildren();
  const show = (title: string, notice: JSX.Element) => {
    document.title = `${title} · NZMPTA AutoRep`;
    render(notice, main);
    return null;
  };

  switch (homeFor(identity)) {
    case "nobody":
      // Signing in is a server POST, so a device has to reach the server once before it can work
      // offline — and after Sign out it has forgotten who was here (by design: the next person to
      // pick up the iPad must not land in someone else's tests).
      return show(
        "Offline",
        <Notice title="You're offline and nobody is signed in here">
          <p>
            AutoRep needs a connection to sign in on this device. Once you've signed in, your tests work here
            with no signal.
          </p>
        </Notice>,
      );
    case "admin":
      return show(
        "Offline",
        <Notice title="The admin portal needs a connection" retry>
          <p>Administration only works online. Reconnect and try again.</p>
        </Notice>,
      );
    case "sync-only": {
      document.title = "Send your tests · NZMPTA AutoRep";
      render(
        <div class="card" style="max-width:640px;margin:0 auto">
          <h1 style="margin-top:0">Your licence has expired</h1>
          <p>
            You can't start new tests until NZMPTA renews your licence. Tests already saved on this device are
            listed below — connect to send them.
          </p>
        </div>,
        main,
      );
      main.appendChild(root("sync-only-root"));
      return null;
    }
  }

  let route = shellRouteFor(location.pathname);
  if (route === "root") {
    // The installed app's start_url is "/", which the server answers with a role redirect. Offline
    // the shell makes the same decision from the identity record, and draws the tester's home in
    // place rather than navigating — a second navigation on weak signal would wait out the
    // worker's timeout all over again.
    history.replaceState(null, "", "/App");
    route = "home";
  }

  switch (route) {
    case "home":
      main.appendChild(root("home-root"));
      break;
    case "tests":
      main.appendChild(root("test-list-root"));
      break;
    case "wizard":
      main.appendChild(root("wizard-root"));
      break;
    case "new":
      return show(
        TITLES.new,
        <Notice title="Starting a test needs a connection">
          <p>
            Choosing a farm for a new test still needs signal. Tests already on this device carry on as normal
            — open one from My tests.
          </p>
        </Notice>,
      );
    default:
      return show(
        "Offline",
        <Notice title="This page needs a connection">
          <p>Your tests are saved on this device — open them from My tests.</p>
        </Notice>,
      );
  }
  document.title = `${TITLES[route]} · NZMPTA AutoRep`;
  return route;
}
