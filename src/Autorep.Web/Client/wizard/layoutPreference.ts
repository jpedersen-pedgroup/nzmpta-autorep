// Which wizard layout this tester prefers. A display preference, not part of the test record — it
// stays on the device and never syncs.
//
// Keyed per tester for the same reason the test database is (see testStore.dbName): tablets get
// passed around a company, and a shared key would hand one tester's layout to whoever signs in
// next. When identity isn't established yet the unnamespaced key is used, matching the store's
// fallback.
import { currentTesterId } from "../db/testStore";
import { DEFAULT_LAYOUT, isWizardLayout, type WizardLayout } from "./shells/types";

const PREFIX = "autorep:wizardLayout";

export function layoutKeyFor(testerId: string | null): string {
  return testerId ? `${PREFIX}:${testerId}` : PREFIX;
}

/** Drop layout keys belonging to anyone but the current tester. purgeStaleLocalData clears departed
 * testers' databases but leaves localStorage alone, so this runs alongside it at startup. Unlike a
 * test database there's nothing here worth retaining — a preference exists nowhere but on screen,
 * so it can always be dropped. No-ops until identity is established. */
export function purgeOtherTesterLayouts(): void {
  const current = currentTesterId();
  if (current === null) return;
  const keep = layoutKeyFor(current);
  try {
    const stale: string[] = [];
    for (let i = 0; i < localStorage.length; i++) {
      const k = localStorage.key(i);
      if (k && k !== keep && (k === PREFIX || k.startsWith(`${PREFIX}:`))) stale.push(k);
    }
    for (const k of stale) localStorage.removeItem(k);
  } catch {
    /* storage unavailable (private mode) — nothing to sweep */
  }
}

export function getLayout(): WizardLayout {
  try {
    const raw = localStorage.getItem(layoutKeyFor(currentTesterId()));
    return isWizardLayout(raw) ? raw : DEFAULT_LAYOUT;
  } catch {
    // Storage can throw in private mode. A layout preference isn't worth failing a test over.
    return DEFAULT_LAYOUT;
  }
}

export function setLayout(layout: WizardLayout): void {
  try {
    localStorage.setItem(layoutKeyFor(currentTesterId()), layout);
  } catch {
    /* preference simply doesn't persist this session */
  }
}
