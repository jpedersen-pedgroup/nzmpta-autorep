// The app header is sticky, so anything else on the page that sticks has to start below it. Its
// height isn't a constant — the logo, the avatar and the sign-out button set it on desktop, and on
// mobile it grows while the burger menu is open — so it's measured rather than guessed and
// published as --app-header-h for stylesheets to offset against.
import { useEffect } from "preact/hooks";

export function useAppHeaderOffset(): void {
  useEffect(() => {
    const root = document.documentElement;
    const header = document.querySelector<HTMLElement>(".app-header");
    // Signed-out shells render no header; 0 leaves sticky bars flush with the viewport top.
    if (!header) {
      root.style.setProperty("--app-header-h", "0px");
      return;
    }
    const measure = () =>
      root.style.setProperty("--app-header-h", `${Math.round(header.getBoundingClientRect().height)}px`);
    measure();
    const observer = new ResizeObserver(measure);
    observer.observe(header);
    return () => {
      observer.disconnect();
      root.style.removeProperty("--app-header-h");
    };
  }, []);
}
