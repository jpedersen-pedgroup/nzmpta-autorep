// Entry point for the AutoRep PWA client bundle (output: wwwroot/js/dist/autorep.js).
// Mounts the offline Preact wizard and the "My tests" list when their roots are present.
import { mountWizard } from "./wizard/WizardApp";
import { mountTestList } from "./ui/TestListApp";

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
