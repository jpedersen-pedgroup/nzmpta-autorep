// Builds the flat fault list for the Fault Aggregator from a LocalTest: every Visual-Fault item
// marked as a fault, every Test Record reading that fails its standard, and the two machine checks
// that are judged outside both — the regulators' suitability and the pulsators' model bands — so
// the summary never says "no faults" over a red result further into the report (MPNZ, Oct 2026).
import type { LocalTest } from "../db/testStore";
import { preStartSections, runningSectionsFor } from "../wizard/visualChecklist";
import { resolveWizard } from "../wizard/wizardStepResolver";
import { allReadingSections } from "../passfail/standards";
import { recommendationFor } from "../reference/faultCatalog";
import { evaluate } from "../passfail/passFail";
import { pulsatorSummary } from "../passfail/pulsatorStats";
import { recordedRows } from "../ui/measurementRows";
import type { FaultInput } from "./faultAggregator";
import { readingRecommendation } from "./readingRecommendations";

/** The keys of the machine-check faults, which carry a recommendation like any other fault. */
export const REGULATOR_FAULT_KEY = "regulatorsSuitable";
export const RATE_BAND_FAULT_KEY = "pulsatorRateBand";
export const RATIO_BAND_FAULT_KEY = "pulsatorRatioBand";

/** What those faults are called where a key needs a name (the amendment history). */
export const MACHINE_CHECK_LABELS: Record<string, string> = {
  [REGULATOR_FAULT_KEY]: "Regulators correct and big enough",
  [RATE_BAND_FAULT_KEY]: "Pulsator rate (model band)",
  [RATIO_BAND_FAULT_KEY]: "Pulsator ratio (model band)",
};

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
          recommendation: test.recommendations[it.key] ?? (recommendationFor(it.lookup, e.observation) || e.note),
        });
      }
    }
  }

  for (const sec of allReadingSections(config, test.readings)) {
    for (const r of sec.readings) {
      const v = test.readings[r.key];
      if (v != null && evaluate(v, r.rule) === "fail") {
        inputs.push({
          key: r.key,
          component: sec.title,
          description: `${r.label}: ${v} ${r.unit}`,
          severity: "Major",
          source: "Numerical tests",
          recommendation: test.recommendations[r.key] ?? readingRecommendation(r, v),
        });
      }
    }
  }

  // The tester's answer to "correct and big enough for this plant?" — only an explicit No.
  if (config.regulatorsSuitable === false) {
    inputs.push({
      key: REGULATOR_FAULT_KEY,
      component: "Regulator",
      description: "Regulators are not correct or not big enough for this plant",
      severity: "Major",
      source: "Machine configuration",
      recommendation:
        test.recommendations[REGULATOR_FAULT_KEY] ?? "Fit regulators of the correct type and capacity for this plant.",
    });
  }

  // Every pulsator must run inside its model's rate and ratio bands (the legacy catalogue's
  // tick/cross); judged on the recorded units, as the report's pulsator table prints it.
  const pulsators = recordedRows(test.pulsatorRows);
  const span = (lo: number | null, hi: number | null) => (lo === hi ? `${lo}` : `${lo}–${hi}`);
  if (pulsators.length > 0) {
    const s = pulsatorSummary(pulsators, config.pulsatorModel);
    if (s.rateBand && s.rateBandOk === false) {
      inputs.push({
        key: RATE_BAND_FAULT_KEY,
        component: "Pulsation",
        description: `Pulsator rate ${span(s.slowestRate, s.fastestRate)} ppm is outside the model band of ${s.rateBand.min}–${s.rateBand.max} ppm`,
        severity: "Major",
        source: "Pulsator results",
        recommendation:
          test.recommendations[RATE_BAND_FAULT_KEY] ?? "Service or replace the pulsators running outside the model's rate band.",
      });
    }
    if (s.ratioBand && s.ratioBandOk === false) {
      inputs.push({
        key: RATIO_BAND_FAULT_KEY,
        component: "Pulsation",
        description: `Pulsator ratio ${span(s.lowestRatio, s.highestRatio)}% is outside the model band of ${s.ratioBand.min}–${s.ratioBand.max}%`,
        severity: "Major",
        source: "Pulsator results",
        recommendation:
          test.recommendations[RATIO_BAND_FAULT_KEY] ?? "Service or replace the pulsators running outside the model's ratio band.",
      });
    }
  }

  return inputs;
}
