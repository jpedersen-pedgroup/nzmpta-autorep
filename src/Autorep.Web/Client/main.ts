// Entry point for the AutoRep PWA client bundle (output: wwwroot/js/dist/autorep.js).
// Loads the synced test standards (cached-then-fresh, with an update notice), then mounts the
// offline Preact wizard and the "My tests" list when their roots are present.
import { initStandards } from "./standards/standardsSync";
import { initEquipment } from "./standards/equipmentSync";
import { initFaultCatalog } from "./standards/faultCatalogSync";
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
    });
  }

  const listRoot = document.getElementById("test-list-root");
  if (listRoot) mountTestList(listRoot);
}

// Purge any other tester's locally-cached data first (shared-device isolation), THEN load the
// synced standards and mount the offline wizard + "My tests" list.
void purgeStaleLocalData()
  .then(() => Promise.allSettled([initStandards(), initEquipment(), initFaultCatalog()]))
  .finally(mountApps);
