// The default recommendation for a reading that fails its standard, so a failed reading never
// reaches the report with a blank action. The tester can still overwrite it on Fault Summary
// (test.recommendations wins, see buildFaults.ts).
//
// Wording is taken from NZMPTA's CMM ratings sheet (plans/reference/cmm-fault-ratings.csv) where
// it has a row for the failure; those are marked "CMM". The rest are drafted in the same style for
// NZMPTA to review, since the sheet has no row (or an empty recommendation) for them.
import type { ReadingDef } from "../passfail/standards";

type Recommend = string | ((reading: ReadingDef, value: number) => string);

// CMM "Cluster" rows: low and high cluster air admission.
const LOW_AIR_ADMISSION =
  "Low cluster air admission may cause vacuum fluctuations in the claw and milking too slow. Clean air admission holes";
const HIGH_AIR_ADMISSION =
  "High cluster air admission will lower the reserve and may also cause milk fat damage. Service cluster air admission holes";
// CMM "Cluster": low cluster air admission and high milk system leaks.
const MILK_SYSTEM_LEAKS =
  "High consumption can be due to leaks in bowls, seals, vacuum taps, rubberware. Check and service as required";
// CMM "Main / Receiver Airline": airline vacuum drop too high.
const AIRLINE_RESTRICTION = "Check for restriction e.g. gland, sanitary trap, undersized pipelines";
// Drafted: the sheet's "Regulation Characteristics" row has no recommendation.
const REGULATION =
  "Check the regulator is fitted to the manufacturer's specifications, then service or replace it, or tune the VSD";
const GAUGE = "Adjust the vacuum gauge to read within 1 kPa of the test gauge, or replace it if it can't be adjusted";
const SERVICE_PULSATORS = "Service or replace Pulsators"; // CMM "Pulsation"

/** Below the band's minimum → the low wording, otherwise the high wording. */
const byDirection =
  (low: string, high: string) =>
  (reading: ReadingDef, value: number): string =>
    (reading.rule.kind === "between" || reading.rule.kind === "atLeast") && reading.rule.min != null && value < reading.rule.min
      ? low
      : high;

const RECOMMENDATIONS: Record<string, Recommend> = {
  // 1 · System vacuum
  "tr.workingVacuum": (r) =>
    `Vacuum level at Receiver is above maximum allowable level. Reduce vacuum to ≤${r.rule.kind === "atMost" ? r.rule.limit : 50} kPa`, // CMM
  "tr.regulationDeviation": "Adjust the regulator so the working vacuum is within 2 kPa of the nominal vacuum",
  "tr.minSpeedVacuum": "Adjust system to conform to ISO standard", // CMM
  // 2 · Reserve
  "tr.effectiveReserve": "Repair any leaks, service or replace vacuum pump with the appropriate size", // CMM
  "tr.regulationLoss": REGULATION,
  "tr.regulatorLeakage": "The regulator is leaking air which will affect the reserve. Service or replace the regulator", // CMM
  // 3 · Regulation
  "tr.fallOff": REGULATION,
  "tr.regulationUndershoot": REGULATION,
  "tr.regulationOvershoot": REGULATION,
  // 4 · Airline drop
  "tr.airlineDropRR": AIRLINE_RESTRICTION,
  "tr.airlinePumpDrop": AIRLINE_RESTRICTION,
  // 5 · Regulator sensitivity
  "tr.regulatorSensitivity": "Service or replace the regulator",
  // 7 · Gauge accuracy (CMM "Vacuum Gauge", both rows)
  "tr.gaugeError1": GAUGE,
  "tr.gaugeError2": GAUGE,
  "tr.gaugeError3": GAUGE,
  // 10–12 · Airflow
  "add.vacuumSystemLeakage": "Repair leaks to within ISO Standard", // CMM
  "add.milkSystemLeakage": MILK_SYSTEM_LEAKS,
  "add.acrConsumption": "Service the ACRs (air rams, tubing and seals) to bring air use within the allowance",
  "add.clusterAirAdmission": byDirection(LOW_AIR_ADMISSION, HIGH_AIR_ADMISSION),
  // 13 · Individual cluster
  "ica.totalAirAdmission": HIGH_AIR_ADMISSION,
  "ica.leakage": MILK_SYSTEM_LEAKS,
  "ica.airVentAdmission": LOW_AIR_ADMISSION,
  // 14–15 · Pulsation
  "puls.pulsatorConsumption": "Service Pulsators", // CMM "High Pulsation System Air Consumption"
  "puls.testPulsationReading": "Check the pulsator airline for restrictions and leaks, and service the pulsators",
  "puls.rateSpread": SERVICE_PULSATORS,
  "puls.ratioSpread": SERVICE_PULSATORS,
  "puls.airlineStability": "No change required unless problems occur, then install a larger Pulsator Airline", // CMM
  // Additional tests
  "add.milkMeter": "Service the milk meters to bring air use within the allowance",
  "add.teatSpray": "Service the teat sprayer to bring air use within the allowance",
  "add.gateCylinder": "Service the gate cylinders to bring air use within the allowance",
  "add.regulatorLoad": "Service or replace the Vacuum Regulator", // CMM
};

/** True when the reading has its own wording rather than the generic fallback. */
export function hasReadingRecommendation(key: string): boolean {
  return key in RECOMMENDATIONS;
}

/** The default action for a failed reading. Readings without their own wording get a plain
 * instruction rather than a blank. */
export function readingRecommendation(reading: ReadingDef, value: number): string {
  const rec = RECOMMENDATIONS[reading.key];
  if (typeof rec === "function") return rec(reading, value);
  return rec ?? "Investigate and bring within the standard";
}
