// Entry point for the AutoRep PWA client bundle (output: wwwroot/js/dist/autorep.js). It runs on
// the server-rendered tester pages, the lapsed-licence page, the admin read-only test view, and in
// the offline shell (wwwroot/app-shell.html) the service worker serves when the server can't
// answer. In order:
//  1. Who is signed in: a server-rendered page says so itself; the shell has only the device's
//     identity record (db/identity.ts).
//  2. Shell only: draw the header and put the page's empty root in place (shell/shell.tsx).
//  3. Purge other testers' local data (shared-device isolation) — which needs (1).
//  4. Apply the CACHED reference data (IndexedDB only) and mount. Nothing up to here waits on the
//     network: a tester on one bar of signal sees their page straight away.
//  5. In the background: confirm the session, refresh the reference data, and tell the mounted
//     apps if anything they render from changed.
import { applyCachedStandards, refreshStandards } from "./standards/standardsSync";
import { applyCachedEquipment, refreshEquipment } from "./standards/equipmentSync";
import { applyCachedFaultCatalog, refreshFaultCatalog } from "./standards/faultCatalogSync";
import { applyCachedPrivacy, refreshPrivacy } from "./standards/privacySync";
import { initFarms } from "./sync/farmsSync";
import { initCalibration } from "./sync/calibrationSync";
import { initCompanyBranding } from "./sync/companyBrandingSync";
import { initTesterDetails } from "./sync/testerDetailsSync";
import { mountWizard } from "./wizard/WizardApp";
import { mountTestList } from "./ui/TestListApp";
import { mountSyncOnly } from "./ui/SyncOnlyApp";
import { mountCompanyTestList } from "./ui/CompanyTestListApp";
import { mountHome } from "./shell/HomePage";
import { mountNewTest } from "./ui/NewTestApp";
import { mountAppStatus } from "./shell/AppStatus";
import { allTests, purgeStaleLocalData } from "./db/testStore";
import { requestPersistentStorage, watchForFullStorage } from "./storage/durability";
import { cachedIdentity, loadIdentity } from "./db/identity";
import { purgeOtherTesterLayouts } from "./wizard/layoutPreference";
import { enableSessionChecks } from "./connectivity";
import { startAutoSync } from "./sync/autoSync";
import { isShellDocument, renderShellChrome, renderShellPage } from "./shell/shell";
import { isTesterPath } from "./shell/routes";
import { REFERENCE_REFRESHED_EVENT, type ReferenceRefreshedDetail } from "./appEvents";

function mountApps(): void {
  const statusRoot = document.getElementById("app-status-root");
  if (statusRoot) mountAppStatus(statusRoot);

  const homeRoot = document.getElementById("home-root");
  if (homeRoot) mountHome(homeRoot);

  const wizardRoot = document.getElementById("wizard-root");
  if (wizardRoot) {
    const params = new URLSearchParams(location.search);
    mountWizard(wizardRoot, {
      id: params.get("id") ?? undefined,
      farmId: params.get("farmId") ?? undefined,
      farmName: params.get("farmName") ?? undefined,
      // Read-only server view (admin, or a tester reading a company colleague's test): fetch the
      // test from the server instead of IndexedDB.
      serverTestId: wizardRoot.getAttribute("data-server-test") ?? undefined,
      backHref: wizardRoot.getAttribute("data-back") ?? undefined,
    });
  }

  const listRoot = document.getElementById("test-list-root");
  if (listRoot) mountTestList(listRoot);

  const newTestRoot = document.getElementById("new-test-root");
  if (newTestRoot) mountNewTest(newTestRoot);

  // Lapsed licence: the only tester surface still reachable (/Account/FinishSync).
  const syncOnlyRoot = document.getElementById("sync-only-root");
  if (syncOnlyRoot) mountSyncOnly(syncOnlyRoot);

  const companyListRoot = document.getElementById("company-test-list-root");
  if (companyListRoot) mountCompanyTestList(companyListRoot);
}

async function warnAboutRetainedWork(): Promise<void> {
  const purge = await purgeStaleLocalData();
  if (!purge.retained?.length) return;
  // That work exists nowhere but this device, and only its owner can send it — a test is
  // attributed to whoever is signed in, so it can never be flushed from this account.
  const known = purge.retained.filter((r) => r.unsyncedCount !== null);
  const total = known.reduce((sum, r) => sum + (r.unsyncedCount ?? 0), 0);
  const unreadable = purge.retained.length - known.length;
  const parts: string[] = [];
  if (total > 0) parts.push(`${total} unsynced test${total === 1 ? "" : "s"}`);
  if (unreadable > 0) parts.push(`data that couldn't be read`);
  const { showToast } = await import("./ui/toast");
  showToast(
    `This device still holds ${parts.join(" and ")} from ` +
      `${purge.retained.length === 1 ? "another tester" : `${purge.retained.length} other testers`}. ` +
      "It can't be sent from your account — they need to sign in and sync.",
    "error",
  );
}

async function boot(): Promise<void> {
  // Before anything can write: a refused write must be shouted about, never swallowed.
  watchForFullStorage();
  const shell = isShellDocument();
  const bootIdentity = await loadIdentity();
  if (shell) {
    renderShellChrome(bootIdentity);
    renderShellPage(bootIdentity); // may move "/" to "/App" — read the path after this
  }

  // The tester app proper keeps the device's own data fresh (farm book, calibration, branding).
  // Path-based, because in the shell the page's roots only exist once the bundle has drawn them.
  // /Admin is excluded: a Super-Administrator's /api/farms is the entire national farm list, and
  // caching that PII into an admin's IndexedDB buys nothing — admins never pick farms offline.
  const testerPage = isTesterPath(location.pathname);

  await warnAboutRetainedWork().catch(() => undefined);
  purgeOtherTesterLayouts();

  // Cached reference data only — IndexedDB, never the network — so the wizard's first render
  // judges readings against the standards this device last synced, not the bundled defaults.
  await Promise.allSettled([applyCachedStandards(), applyCachedEquipment(), applyCachedFaultCatalog(), applyCachedPrivacy()]);
  mountApps();

  if (testerPage) {
    // Once there is real work on this device, ask the browser to keep it (best-effort; see
    // storage/durability.ts). Not before: an empty install has nothing to protect yet.
    void allTests()
      .then((tests) => (tests.length > 0 ? requestPersistentStorage() : null))
      .catch(() => null);

    // Tests captured offline go up by themselves once the connection is back (sync/autoSync.ts).
    const autoSync = startAutoSync();

    void enableSessionChecks().then(async (connection) => {
      // The shell drew this page for whoever the device's record named. If the server now says
      // someone else is signed in (they signed in while the record still named the previous
      // tester), start again as them — but only once the new record is really stored, or a device
      // that can't write it would reload for ever.
      if (shell && cachedIdentity()?.testerId !== bootIdentity?.testerId) {
        const stored = await loadIdentity();
        if (stored && stored.testerId !== bootIdentity?.testerId) {
          location.reload();
          return;
        }
      }
      // The session is good: send anything still waiting on this device.
      if (connection === "online") void autoSync.trigger("page load");
    });
  }

  const refreshes: Array<() => Promise<boolean | void>> = [refreshStandards, refreshEquipment, refreshFaultCatalog, refreshPrivacy];
  if (testerPage) refreshes.push(initFarms, initCalibration, initCompanyBranding, initTesterDetails);
  const results = await Promise.allSettled(refreshes.map((refresh) => refresh()));
  const changed = results.some((r) => r.status === "fulfilled" && r.value !== false);
  dispatchEvent(new CustomEvent<ReferenceRefreshedDetail>(REFERENCE_REFRESHED_EVENT, { detail: { changed } }));
}

void boot();
