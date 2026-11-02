// Simple tabbed panel for keeping a long wizard step's content within the card (one section
// at a time) instead of one long scroll past the step rail.
//
// A shell with Back/Next buttons wraps the step in a TabNavProvider, and those buttons then walk the
// step's tabs before moving on to the next step (Jono, 2 Nov 2026). Without a provider — the single
// scroll layout, which has no such buttons — tabs just work by themselves.
import { createContext } from "preact";
import { useContext, useLayoutEffect, useRef, useState } from "preact/hooks";
import type { ComponentChildren } from "preact";

export interface TabDef {
  key: string;
  label: string;
  content: ComponentChildren;
}

/** Where the step on screen is in its tabs. */
interface TabPlace {
  index: number;
  labels: string[];
  goTo: (index: number) => void;
}

interface TabNavLink {
  report: (place: TabPlace | null) => void;
  /** True for a step entered by going Back: it opens on its last tab. */
  entersAtEnd: () => boolean;
}

const TabNavContext = createContext<TabNavLink | null>(null);
export const TabNavProvider = TabNavContext.Provider;

export interface TabNav {
  link: TabNavLink;
  /** The label of the tab Next would open; null when there's no tab left in this step. */
  nextTab: string | null;
  hasPrevTab: boolean;
  /** The next tab, or `leave` when the step has none left. */
  next: (leave: () => void) => void;
  /** The previous tab, or `leave` when the step has none: the step it goes to opens on its last tab. */
  back: (leave: () => void) => void;
}

/** For a shell: the Back/Next behaviour over the tabs of `step`, the step on screen. */
export function useTabNav(step: string): TabNav {
  const [place, setPlace] = useState<TabPlace | null>(null);
  const enterAtEnd = useRef(false);
  const link = useRef<TabNavLink>({ report: setPlace, entersAtEnd: () => enterAtEnd.current }).current;

  // Tabs reads this as it mounts, which is before this runs. A step with no tabs leaves it unread,
  // and it must not carry over to some later step.
  useLayoutEffect(() => {
    enterAtEnd.current = false;
  }, [step]);

  const index = place?.index ?? 0;
  const nextTab = place?.labels[index + 1] ?? null;
  const hasPrevTab = index > 0;
  return {
    link,
    nextTab,
    hasPrevTab,
    next: (leave) => {
      if (nextTab != null) place?.goTo(index + 1);
      else leave();
    },
    back: (leave) => {
      if (hasPrevTab) {
        place?.goTo(index - 1);
      } else {
        enterAtEnd.current = true;
        leave();
      }
    },
  };
}

export function Tabs({ tabs }: { tabs: TabDef[] }) {
  const nav = useContext(TabNavContext);
  const [active, setActive] = useState(() => (nav?.entersAtEnd() ? tabs[tabs.length - 1]?.key : tabs[0]?.key) ?? "");
  const current = tabs.find((t) => t.key === active) ?? tabs[0];
  const stripRef = useRef<HTMLDivElement>(null);
  const reveal = useRef(false);

  const index = current ? tabs.indexOf(current) : 0;
  const labels = tabs.map((t) => t.label);
  const keys = useRef<string[]>([]);
  keys.current = tabs.map((t) => t.key);

  // Compared by value: the tab list is rebuilt on every render.
  useLayoutEffect(() => {
    nav?.report({
      index,
      labels,
      goTo: (i) => {
        reveal.current = true;
        setActive(keys.current[i]);
      },
    });
    return () => nav?.report(null);
  }, [nav, index, labels.join("\n")]);

  // Next sits below the tab, so the one it opens has to be brought into view: the strip scrolls
  // sideways on a long step, and the page may be well down a long tab.
  useLayoutEffect(() => {
    if (!reveal.current) return;
    reveal.current = false;
    stripRef.current?.querySelector(".tab.is-active")?.scrollIntoView?.({ block: "nearest", inline: "nearest" });
  }, [current?.key]);

  return (
    <div>
      <div class="tabs" role="tablist" ref={stripRef}>
        {tabs.map((t) => (
          <button
            key={t.key}
            type="button"
            role="tab"
            aria-selected={t.key === current?.key}
            class={"tab" + (t.key === current?.key ? " is-active" : "")}
            onClick={() => setActive(t.key)}
          >
            {t.label}
          </button>
        ))}
      </div>
      <div class="tab-panel" key={current?.key}>{current?.content}</div>
    </div>
  );
}
