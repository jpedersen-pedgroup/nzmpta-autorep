// The contract every wizard layout implements. A shell owns the page chrome — header, step
// navigation, footer — and nothing else: it renders step bodies through ctx and never collects
// data itself. That boundary is what makes switching layout mid-test safe.
import type { VNode } from "preact";
import type { StepContext } from "../WizardSteps";
import type { WizardStep } from "../types";

/** Which layout the Tester is running. Stored per tester; "rail" is the default. */
export type WizardLayout = "rail" | "scroll" | "hub";

export interface LayoutOption {
  id: WizardLayout;
  name: string;
  /** One line on what working in this layout is like, shown in the cog menu. */
  blurb: string;
  /** Font Awesome icon class for the menu row. */
  icon: string;
}

export const LAYOUT_OPTIONS: LayoutOption[] = [
  {
    id: "rail",
    name: "Step rail",
    blurb: "Numbered steps down the side, one panel at a time",
    icon: "fa-list-ol",
  },
  {
    id: "scroll",
    name: "Single scroll",
    blurb: "One continuous page — open the sections you need",
    icon: "fa-arrows-up-down",
  },
  {
    id: "hub",
    name: "Task hub",
    blurb: "Overview of every step, tap in to work on one",
    // fa-grid-2 would suit better but it's Pro-only, and the vendored Free set is the fallback.
    icon: "fa-table-cells-large",
  },
];

export const DEFAULT_LAYOUT: WizardLayout = "rail";

export function isWizardLayout(value: unknown): value is WizardLayout {
  return value === "rail" || value === "scroll" || value === "hub";
}

export interface ShellProps {
  /** Everything a step body needs — the shell passes this straight to renderStep(). */
  ctx: StepContext;
  current: WizardStep;
  onGo(step: WizardStep): void;
  online: boolean;
  /** Cog + layout pop-out. Each shell places it in its own header. */
  layoutMenu: VNode;
  /** Short-test / read-only / calibration notices. Shared wording, shell chooses placement. */
  banners: VNode;
  /** Connection state in words — "Offline" means something different on a server view, where the
   * test was never stored on this device, so the wording is decided once and shared. */
  connectionLabel: string;
  /** Where Exit/Back leads, and what to call it — a server view changed nothing, so it's "Back". */
  exitHref: string;
  exitLabel: string;
}
