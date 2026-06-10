// Severity + recommendation defaults for the standard visual-fault observations, seeded from the
// CMM fault-ratings catalog (plans/reference/cmm-fault-ratings.csv). The CMM catalog isn't a clean
// 1:1 join to the Lookup observations, so this is a curated first pass: Major is the CMM default,
// with explicit Minor/Critical exceptions, plus CMM-style recommendations for common faults. All
// values remain editable by the Tester. Unknown observations fall back to Major / no recommendation
// (so a typo is harmless, not a crash).
import type { FaultSeverity } from "../wizard/types";

/** Observations the CMM catalog rates as cosmetic / wear / cleaning items. */
const MINOR_FAULTS = new Set<string>([
  // Vacuum pump oil/water/wicks
  "Oil/Water Dirty",
  "Oil/Water Level Incorrect",
  "Oil/Water Too Low",
  "Oil/Water Too High",
  "Oil/Water Supply Should Be Protected",
  "Lid Not Fitted",
  "No Lid",
  "Oil Wicks Dirty",
  "Vacuum Pump Coupling Perishing/Split",
  "Interceptor Connection Is Loose",
  // Belts (tension only)
  "Vee Belts Require Retensioning",
  "Vee Belts Loose",
  "Vee Belts Overtightened",
  // Releaser plumbing wear
  "Rubber Bends/Unions Perished",
  "Unions Leaking",
  "Spring Is Broken",
  // Main airline
  "Dead End Lengths Exceed 75 Mm",
  "Not Falling To A Drain Point",
  "Bracket Is Loose",
  // Inlets / claw wear
  "Perishing/Split",
  "Perishing",
  "Split",
  "Valves Require Servicing",
  // Liner alignment + shell dent
  "Reset Liner Alignment Guide Arrows",
  "Shells Dented",
  // Long milk tubes (CMM rates these minor)
  "Long Milk Tubes Incorrect Length",
  "Long Milk Tubes Perished/Split",
  "Long Milk Tube Condition Poor",
  // Platform
  "Rollers Very Noisy",
  "Platform Should Be Serviced",
  // Milk-flow indicators
  "Indicators Cloudy",
  "Indicator Condition Is Poor",
  "Seals Leaking",
  "Seals Dirty",
  "Seals Missing",
  "Indicator Installation Poor",
  // ACR + milk meters
  "Ropes Frayed",
  "Ropes Broken",
  "Hard To Pull Down",
  "Ram Condition Is Poor",
  "Tube Condition Is Poor",
  "Float Condition Is Poor",
  "Milk Meter Condition Is Poor",
  // Pulsation servicing items
  "Filtered Air System Requires Servicing",
  "Brackets Are Broken",
  "Filters Are Dirty",
  "Filters Are Perishing",
  "Not The Recommended Pulsator",
  "Service Overdue",
  "Run Time Exceeds Recommendation",
  // Vacuum gauge cosmetic
  "Vacuum Gauge Damaged/Rusty",
  // Regulator filters
  "Filters Dirty",
  "Filters Perished",
  "Filters Require Cleaning/Replacing",
  // Receiver cosmetic + seals
  "Face Plate Is Cloudy",
  "Face Plate Seal Condition Is Poor",
  "Face Plate Seal Is Perished/Split",
  "Face Plate Seal Is Dirty",
  // VP running oil/water
  "Interceptor Support Is Poor",
  "Interceptor Not Bracketted",
  "Water Flow Rate Poor",
  "Restriction Inside Vacuum Pump",
  "Restriction Inside Exhaust",
  // Jetters
  "Condition Poor – Servicing Required",
]);

/** Reserved for genuinely critical visual findings (none in the standard lists today). */
const CRITICAL_FAULTS = new Set<string>([]);

export function severityForObservation(observation: string | null | undefined): FaultSeverity {
  if (!observation) return "Major";
  if (CRITICAL_FAULTS.has(observation)) return "Critical";
  if (MINOR_FAULTS.has(observation)) return "Minor";
  return "Major";
}

/** CMM-style recommendation wording for common standard faults. Empty string = let the Tester type
 * one (the fault description itself is already shown in the Fault Summary). */
const RECOMMENDATIONS: Record<string, string> = {
  "Oil/Water Condition Poor": "Service the vacuum pump oiler / change oil",
  "Oil/Water Dirty": "Change vacuum pump oil / water",
  "Oil/Water Too Low": "Raise level to 20 mm above the pump shaft centreline",
  "Oil/Water Too High": "Lower level to 20 mm above the pump shaft centreline",
  "Oil Wicks Dirty": "Clean oil wicks",
  "Oil Wicks Require Replacement": "Replace oil wicks",
  "No Oil Wicks Present": "Fit oil wicks",
  "Lid Not Fitted": "Fit a lid to the oil/water supply",
  "No Lid": "Fit a lid to the oil/water supply",
  "Vee Belts Perishing/Split": "Replace vee belts",
  "Vee Belts Require Replacement": "Replace vee belts",
  "Vee Belt Perishing/Split": "Replace vee belts",
  "Vee Belt Condition Poor - Replace": "Replace vee belts",
  "Vee Belts Require Retensioning": "Re-tension vee belts",
  "Vee Belts Loose": "Tighten vee belts",
  "Vee Belts Overtightened": "Re-tension vee belts to specification",
  "Guards Not Fitted To Osh Regulations": "Install guards to WorkSafe requirements",
  "Guard Not Fitted": "Install guard",
  "No Guard": "Install guard",
  "End Play Excessive - Service Pump": "Service the vacuum pump (excessive end play)",
  "Excessive": "Service the vacuum pump (excessive end play)",
  "Re-Plumb Vacuum Pump Exhaust": "Remove restrictions / re-plumb the vacuum pump exhaust",
  "Vacuum Pump Coupling Faulty": "Replace the vacuum pump coupling",
  "Vacuum Pump Coupling Perishing/Split": "Replace coupling inserts",
  "Releaser Milk Pump Intake Line Is Leaking": "Repair the releaser milk pump intake line",
  "Non Return Valve Is Faulty": "Service or replace the non-return valve",
  "Blocked With Debris - Requires Cleaning": "Clean the non-return valve",
  "Spring Is Broken": "Replace the non-return valve spring",
  "Direction Of Rotation Incorrect": "Reverse rotation to the correct direction",
  "Direction Of Rotation Is Incorrect": "Reverse rotation to the correct direction",
  "Backing Plate Is Missing/Faulty": "Fit / repair the backing plate",
  "Backing Plate Is Back To Front": "Refit the backing plate the correct way around",
  "Cluster Air Admission Is Blocked": "Clean the cluster air-admission holes",
  "No Cluster Air Admission": "Clear / fit cluster air admission",
  "Cluster Air Admission Requires Servicing": "Service the cluster air-admission holes",
  "Pulse Tube Manifold Blocked": "Clear the pulse-tube manifold",
  "Pulse Tube Manifold Leaking": "Service the pulse-tube manifold",
  "Liners Not Compatible With Shells": "Replace liners with a shell-compatible type",
  "Perished/Split": "Replace liners",
  "Liners Require Replacement": "Replace liners",
  "Liners Have Minimal Elasticity": "Replace liners (minimal elasticity)",
  "Short Pulse Tubes Perished/Split": "Replace the short pulse tubes",
  "Long Pulse Tubes Perished/Split": "Replace the long pulse tubes",
  "Long Milk Tubes Perished/Split": "Replace the long milk tubes",
  "Filter Condition Is Poor/Blocked": "Clean or replace the pulsator air filters",
  "Filters Are Dirty": "Clean the air filters",
  "Filters Are Perished": "Replace the air filters",
  "Vacuum Gauge Not Fitted": "Fit a vacuum gauge",
  "Vacuum Gauge Faulty": "Replace the vacuum gauge",
  "Vacuum Gauge Damaged/Rusty": "Replace the vacuum gauge",
  "Vacuum Gauge Position Incorrect": "Reposition the vacuum gauge so milkers can see it",
  "Not Fitted To Specification": "Refit the regulator to manufacturer's specification",
  "Receiver Is Undersized": "Replace with a correctly sized receiver",
  "Vacuum Pump Noise Is Excessive": "Service the vacuum pump",
  "Vacuum Pump Water Temp Is Excessive": "Investigate / correct vacuum pump water temperature",
  "Incompatible Jetter Type": "Fit compatible jetters",
  "Condition Poor – Servicing Required": "Service the cleaning jetters",
};

export function recommendationForObservation(observation: string | null | undefined): string {
  if (!observation) return "";
  return RECOMMENDATIONS[observation] ?? "";
}
