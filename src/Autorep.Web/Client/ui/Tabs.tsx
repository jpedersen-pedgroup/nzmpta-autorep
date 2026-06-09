// Simple tabbed panel for keeping a long wizard step's content within the card (one section
// at a time) instead of one long scroll past the step rail.
import { useState } from "preact/hooks";
import type { ComponentChildren } from "preact";

export interface TabDef {
  key: string;
  label: string;
  content: ComponentChildren;
}

export function Tabs({ tabs }: { tabs: TabDef[] }) {
  const [active, setActive] = useState(tabs[0]?.key ?? "");
  const current = tabs.find((t) => t.key === active) ?? tabs[0];

  return (
    <div>
      <div class="tabs" role="tablist">
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
