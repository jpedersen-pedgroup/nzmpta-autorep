// Entry point for the AutoRep PWA client bundle (output: wwwroot/js/dist/autorep.js).
// Loads the synced test standards (cached-then-fresh, with an update notice), then mounts the
// offline Preact wizard and the "My tests" list when their roots are present.
import { initStandards } from "./standards/standardsSync";
import { initEquipment } from "./standards/equipmentSync";
import { initFaultCatalog } from "./standards/faultCatalogSync";
import { initPrivacy } from "./standards/privacySync";
import { initFarms } from "./sync/farmsSync";
import { initCalibration } from "./sync/calibrationSync";
import { mountWizard } from "./wizard/WizardApp";
import { mountTestList } from "./ui/TestListApp";
import { purgeStaleLocalData } from "./db/testStore";

function mountApps(): void {
  const wizardRoot = document.getElementById("wizard-root");
  if (wizardRoot) {
    const params = new URLSearchParams(location.search);
    mountWizard(wizardRoot, {
      id: params.get("id") ?? undefined,
      farmId: params.get("farmId") ?? undefined,
      farmName: params.get("farmName") ?? undefined,
      // Admin read-only view: fetch the test from the server instead of IndexedDB.
      serverTestId: wizardRoot.getAttribute("data-server-test") ?? undefined,
    });
  }

  const listRoot = document.getElementById("test-list-root");
  if (listRoot) mountTestList(listRoot);
}

// Purge any other tester's locally-cached data first (shared-device isolation), THEN load the
// synced reference data and mount the offline wizard + "My tests" list. The farm book is
// tester-pages-only: on the admin read-only test view (wizard root with data-server-test) the
// bundle also runs, and for a Super-Admin /api/farms is the entire national list — caching that
// PII into an admin's IndexedDB buys nothing (admins never pick farms offline).
const wizardHost = document.getElementById("wizard-root");
const isTesterPage =
  document.getElementById("test-list-root") !== null ||
  (wizardHost !== null && !wizardHost.getAttribute("data-server-test"));
const referenceSyncs = [initStandards, initEquipment, initFaultCatalog, initPrivacy];
if (isTesterPage) referenceSyncs.push(initFarms, initCalibration);
void purgeStaleLocalData()
  .then((purge) => {
    if (purge.retained) {
      // Their tests exist nowhere but this device, so someone has to sign back in and sync.
      const n = purge.retained.unsyncedCount;
      void import("./ui/toast").then(({ showToast }) =>
        showToast(
          `The previous tester still has ${n} unsynced test${n === 1 ? "" : "s"} on this device. ` +
            `${n === 1 ? "It" : "They"} can't be sent from your account — have them sign in and sync.`,
          "error",
        ),
      );
    }
    return Promise.allSettled(referenceSyncs.map((sync) => sync()));
  })
  .finally(mountApps);
