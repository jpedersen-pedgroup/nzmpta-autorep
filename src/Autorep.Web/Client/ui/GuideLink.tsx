// A direct link to the tester work instructions, for the surfaces that keep working offline (My
// tests and the wizard header). It opens the PDF itself, not the Help page: page HTML is never
// cached, but the service worker answers /guides/* from its saved copy with no signal.
import { guideHref, testerGuide } from "../guides/guides";

interface Props {
  /** "icon": the square outlined button beside the wizard's layout cog. "button": a labelled btn. */
  variant: "icon" | "button";
}

export function GuideLink({ variant }: Props) {
  const guide = testerGuide();
  if (!guide) return null;
  const label = `Open the ${guide.title.toLowerCase()} (PDF, new tab)`;
  return variant === "icon" ? (
    <a
      class="layout-menu__trigger guide-link"
      href={guideHref(guide)}
      target="_blank"
      rel="noopener"
      aria-label={label}
      title={guide.title}
    >
      <i class="fa-solid fa-circle-question" aria-hidden="true" />
    </a>
  ) : (
    <a class="btn btn--secondary btn--sm" href={guideHref(guide)} target="_blank" rel="noopener" aria-label={label}>
      <i class="fa-solid fa-book-open" aria-hidden="true" /> Tester guide
    </a>
  );
}
