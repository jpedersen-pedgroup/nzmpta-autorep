// The tester home page (/App). Drawn by the bundle on the server-rendered page and in the offline
// shell alike, so the two can't drift.
import { render } from "preact";

export function mountHome(root: HTMLElement): void {
  render(<HomePage />, root);
}

function Tile({ href, icon, title, desc }: { href: string; icon: string; title: string; desc: string }) {
  return (
    <a class="tile" href={href}>
      <div class="tile__icon">
        <i class={`fa-solid ${icon}`} aria-hidden="true"></i>
      </div>
      <div class="tile__title">{title}</div>
      <div class="tile__desc">{desc}</div>
    </a>
  );
}

export function HomePage() {
  return (
    <>
      <section class="welcome">
        <h1>Welcome back</h1>
        <p>Pick up where you left off, or start a fresh test on a new farm.</p>
      </section>

      <div class="tile-grid">
        <Tile href="/App/Tests/New" icon="fa-file-circle-plus" title="Start a new test" desc="Begin a fresh machine test at a farm." />
        <Tile href="/App/Tests" icon="fa-list-check" title="My tests" desc="View tests you've created and synced." />
        <Tile
          href="/App/Tests/Company"
          icon="fa-users"
          title="Company tests"
          desc="Read completed tests from everyone at your company. Needs a connection."
        />
        <Tile
          href="/Help"
          icon="fa-book-open"
          title="Help & guides"
          desc="The tester work instructions: running, signing off and syncing machine tests."
        />
      </div>
    </>
  );
}
