// Builds the flat fault list for the Fault Aggregator from a LocalTest: every Visual-Fault item
// marked as a fault, plus every Test Record reading that fails its standard.
import type { LocalTest } from "../db/testStore";
import { preStartSections, runningSectionsFor } from "../wizard/visualChecklist";
import { resolveWizard } from "../wizard/wizardStepResolver";
import { allReadingSections } from "../passfail/standards";
import { evaluate } from "../passfail/passFail";
import type { FaultInput } from "./faultAggregator";

export function buildFaultInputs(test: LocalTest): FaultInput[] {
  const config = test.config;
  const inputs: FaultInput[] = [];

  const runningKeys =
    resolveWizard(config).steps.find((s) => s.step === "VisualFaultsRunning")?.sections ?? [];
  const visualSections = [...preStartSections(config.hasReleaserPump), ...runningSectionsFor(runningKeys)];

  for (const sec of visualSections) {
    for (const it of sec.items) {
      const e = test.visualFaults[it.key];
      if (e?.status === "fault") {
        inputs.push({
          key: it.key,
          component: sec.title,
          description: e.observation ? `${it.label}: ${e.observation}` : it.label,
          severity: e.severity ?? "Major",
          source: "Visual faults",
          recommendation: test.recommendations[it.key] ?? e.note,
        });
      }
    }
  }

  for (const sec of allReadingSections(config)) {
    for (const r of sec.readings) {
      const v = test.readings[r.key];
      if (v != null && evaluate(v, r.rule) === "fail") {
        inputs.push({
          key: r.key,
          component: sec.title,
          description: `${r.label}: ${v} ${r.unit}`,
          severity: "Major",
          source: "Numerical tests",
          recommendation: test.recommendations[r.key],
        });
      }
    }
  }

  return inputs;
}
