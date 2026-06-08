// Entry point for the AutoRep PWA client bundle (output: wwwroot/js/dist/autorep.js).
// Grows in later phases: IndexedDB store, the Preact offline wizard, and the sync client.
// For now it just wires up the shared modules so the bundle builds end-to-end.
import { resolveWizard } from "./wizard/wizardStepResolver";

// Exposed on window for quick manual checks during development.
(globalThis as unknown as { autorep?: unknown }).autorep = { resolveWizard };
