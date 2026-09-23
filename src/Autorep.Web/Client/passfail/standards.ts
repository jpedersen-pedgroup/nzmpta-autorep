// Test-Record reading definitions + the standards that drive their pass/fail rules.
// Thresholds verified 10 Jun 2026 against the NZMPTA Milking Machine Testing Standards Manual
// ("manual", page refs) and ISO 6690:2007 Annex D ("ISO") — see plans/reference/standards-audit.md.
// Later phases move these standards to admin-managed reference data synced into IndexedDB.
import type { MachineConfiguration } from "../wizard/types";
import { correctionFactorFor } from "../reference/lookups";
import { releaserRequirement } from "../reference/standardsData";
import { paramFor, ruleFor } from "./standardsOverrides";
import { pulsationLimits } from "./pulsatorStats";
import { oemPumpCapacity, vacuumPumpFor, type VacuumPumpModel } from "../reference/standardsData";
import { vacuumPumpRows } from "../wizard/pumpRows";
import type { PassFailRule } from "./passFail";

// Required Effective Reserve (L/min) by cluster count — manual p42 table (steps of 2 clusters;
// odd counts round up to the next even row, conservative). Index 0 = 1 cluster.
const EFFECTIVE_RESERVE: number[] = [
  260, 260, 320, 320, 380, 380, 440, 440, 500, 500,
  520, 520, 540, 540, 560, 560, 580, 580, 600, 600,
  650, 650, 700, 700, 750, 750, 800, 800, 850, 850,
  900, 900, 950, 950, 1000, 1000, 1050, 1050, 1100, 1100,
  1150, 1150, 1200, 1200, 1250, 1250, 1300, 1300, 1350, 1350,
  1400, 1400, 1450, 1450, 1500, 1500, 1550, 1550, 1600, 1600,
  1650, 1650, 1700, 1700, 1750, 1750, 1800, 1800, 1850, 1850,
  1900, 1900, 1950, 1950, 2000, 2000, 2050, 2050, 2100, 2100,
];

export function requiredEffectiveReserve(clusters: number): number | null {
  if (clusters < 1) return null;
  // Manual p42: above 80 clusters the requirement keeps growing — 2100 + 25 L/min per cluster.
  if (clusters > 80) return 2100 + (clusters - 80) * 25;
  return EFFECTIVE_RESERVE[clusters - 1] ?? null;
}

/** Cleaning Reserve (L/min) — manual p43: required when a wash-solution injection (slug) system
 * is fitted. CR = π/4 × d² × 8 × ((100 − v)/100) × 0.06 with d = milkline INTERNAL diameter (mm,
 * = OD − 2 mm wall — reproduces the manual's worked examples exactly: OD75 @44 → 1125, OD50 @46
 * → 469) and v = working vacuum rounded UP. */
export function cleaningReserve(
  milklineSize: string | null | undefined,
  workingVacuum: number | null | undefined,
): number | null {
  const od = Number(milklineSize);
  if (!od || od <= 2 || workingVacuum == null || Number.isNaN(workingVacuum)) return null;
  const id = od - 2;
  const v = Math.ceil(workingVacuum);
  return Math.round((Math.PI / 4) * id * id * 8 * ((100 - v) / 100) * 0.06);
}

/** Manual p41: ACR / milk-meter / cluster-restraint allowance — 7.5 L/min per component with a
 * 30 L/min minimum, rounded up to the nearest 10; doubled when bail-gate rams are present. */
export function ancillaryAllowance(componentCount: number, hasBailGates: boolean): number | null {
  if (componentCount < 1) return null;
  const perUnit = paramFor("param.ancillary.perUnit", 7.5);
  const minTotal = paramFor("param.ancillary.minTotal", 30);
  const base = Math.ceil(Math.max(minTotal, perUnit * componentCount) / 10) * 10;
  return hasBailGates ? base * 2 : base;
}

export interface ReadingDef {
  key: string;
  label: string;
  unit: string;
  rule: PassFailRule;
  hint?: string;
  /** Calculated from other readings rather than typed — the formula the tester sees, in the
   * flowchart's terms ("1a − 1b"). The calculation itself lives in derived.ts; a test pins the two
   * lists to each other. */
  derived?: string;
}

/** A per-unit standard applied to a machine total: every threshold × the unit count. */
function perUnitTotal(rule: PassFailRule, units: number): PassFailRule {
  switch (rule.kind) {
    case "atMost":
      return rule.limit == null ? rule : { kind: "atMost", limit: rule.limit * units };
    case "atLeast":
      return rule.min == null ? rule : { kind: "atLeast", min: rule.min * units };
    case "between":
      return rule.min == null || rule.max == null ? rule : { kind: "between", min: rule.min * units, max: rule.max * units };
    default:
      return rule;
  }
}

function describeLimit(rule: PassFailRule): string {
  switch (rule.kind) {
    case "atMost": return `≤ ${rule.limit}`;
    case "atLeast": return `≥ ${rule.min}`;
    case "between": return `${rule.min}–${rule.max}`;
    default: return "";
  }
}
export interface ReadingSection {
  key: string;
  title: string;
  readings: ReadingDef[];
}

/** How many pumps get a calculated OEM-capacity row. The derived-reading table is static, so the
 * per-pump family has to be bounded; legacy recorded four pumps, eight is headroom. */
export const MAX_OEM_PUMPS = 8;
/** The formula shown on the OEM row - the calculation itself lives in derived.ts. */
export const OEM_CAPACITY_FORMULA = "8c x the catalogue's L/min per rpm";

/** What the catalogue says about this pump, and the caveat that it carries no verdict. */
function oemHint(pump: VacuumPumpModel, rpm: number | undefined): string {
  const parts = [`${pump.make} ${pump.model}: ${pump.airFlow} L/min per rpm`];
  if (pump.minRpm != null && pump.maxRpm != null) {
    parts.push(
      pump.minRpm === pump.maxRpm
        ? `catalogue speed ${pump.maxRpm} rpm`
        : `catalogue ${pump.minRpm}-${pump.maxRpm} rpm`,
    );
    if (rpm != null && (rpm < pump.minRpm || rpm > pump.maxRpm)) parts.push("8c is outside that range");
  }
  // Jono, 23 Sep 2026: the arithmetic is right, but a pump works along a curve, so a single figure
  // is not a clean min/max. Kept as a guide until a pump supplier confirms how to judge it.
  parts.push("a guide only, no verdict - pumps work along a curve");
  return parts.join(" · ");
}

/** Test Record readings — the full ISO numerical workflow groups 1–9, each reading tagged with its
 * flowchart ref (1a…9b). Rules verified against the NZMPTA manual + ISO 6690 Annex D; `readings`
 * feeds the standards that depend on other entered values (manual reserve, working vacuum, the
 * atmospheric correction). Section keys mirror the WizardStepResolver. */
export function testRecordSections(
  config: MachineConfiguration,
  readings: Record<string, number> = {},
): ReadingSection[] {
  const sections: ReadingSection[] = [];
  const workingVacuum = readings["tr.workingVacuum"];
  const manualReserve = readings["tr.manualReserve"];

  // Regulation-loss / regulator-leakage allowances (manual p40/p39 / ISO C.4.6 + C.4.8).
  const lossPct = paramFor("param.reserve.lossPct", 10);
  const lossFloor = paramFor("param.reserve.lossFloor", 35);
  const leakPct = paramFor("param.reserve.leakPct", 5);
  const leakFloor = paramFor("param.reserve.leakFloor", 35);
  const lossLimit = (mr: number) => Math.max(lossFloor, Math.round((lossPct / 100) * mr));
  const leakLimit = (mr: number) => Math.max(leakFloor, Math.round((leakPct / 100) * mr));

  // 1 — System vacuum levels
  sections.push({
    key: "SystemVacuumLevels",
    title: "1 · System vacuum",
    readings: [
      {
        key: "tr.workingVacuum",
        label: "Working vacuum @ receiver (1a)",
        unit: "kPa",
        // Manual p40: hard maximum 50 kPa; 40–50 is the guideline band by milkline lift height.
        hint: `max ${ruleFor("tr.workingVacuum", { kind: "atMost", limit: 50 }).limit} — guideline 40–50 by lift height`,
        rule: ruleFor("tr.workingVacuum", { kind: "atMost", limit: 50 }),
      },
      { key: "tr.nominalVacuum", label: "Nominal vacuum (1b)", unit: "kPa", rule: { kind: "none" } },
      {
        key: "tr.regulationDeviation",
        label: "Vacuum regulation deviation (1c)",
        unit: "kPa",
        derived: "1a − 1b",
        // Manual p40 / ISO D.2.7: ±2 kPa (signed difference from nominal).
        hint: `± ${ruleFor("tr.regulationDeviation", { kind: "tolerance", target: 0, tolerance: 2 }).tolerance} kPa`,
        rule: ruleFor("tr.regulationDeviation", { kind: "tolerance", target: 0, tolerance: 2 }),
      },
      { key: "tr.vacuumAtRegulator", label: "Vacuum @ regulator (1d)", unit: "kPa", rule: { kind: "none" } },
      { key: "tr.vacuumAtPump", label: "Vacuum @ pump (1e)", unit: "kPa", rule: { kind: "none" } },
    ],
  });

  // 1f — Minimum pump-speed vacuum (VSD only). Manual p40: rise must be < 2 kPa.
  if (config.vsdFitted) {
    const maxRise = paramFor("param.vsd.maxRise", 2);
    sections.push({
      key: "MinPumpSpeedVacuum",
      title: "1 · Min pump-speed vacuum",
      readings: [
        {
          key: "tr.minSpeedVacuum",
          label: "Vacuum @ minimum VSD speed (1f)",
          unit: "kPa",
          hint:
            workingVacuum != null
              ? `rise < ${maxRise} kPa above working vacuum (${workingVacuum})`
              : "enter working vacuum (1a) for the standard",
          rule: workingVacuum != null ? { kind: "atMost", limit: workingVacuum + maxRise } : { kind: "none" },
        },
      ],
    });
  }

  // 2 — Reserve characteristics. The required reserve is the HIGHER of the effective-reserve
  // table value and the Cleaning Reserve when a flushing/wash-injection system is fitted (manual
  // p43), and the MEASURED airflow must be atmosphere-corrected (× factor) before comparison
  // (manual p31 / ISO 5.3.2) — equivalently the raw threshold is required ÷ factor.
  const erTable = requiredEffectiveReserve(config.clusterCount);
  const cr = config.flushingPulsationSystem ? cleaningReserve(config.milklineSize, workingVacuum) : null;
  const requiredReserve = erTable != null ? Math.max(erTable, cr ?? 0) : null;
  const atmosFactor = correctionFactorFor(config.atmosPressureSeaLevel) ?? 1;
  const rawReserveMin = requiredReserve != null ? Math.ceil(requiredReserve / atmosFactor) : null;
  const reserveHintParts: string[] = [];
  if (requiredReserve != null) {
    reserveHintParts.push(
      cr != null && cr > (erTable ?? 0)
        ? `cleaning reserve governs: ≥ ${requiredReserve}`
        : `needs ≥ ${requiredReserve} for ${config.clusterCount} clusters`,
    );
    if (atmosFactor !== 1) reserveHintParts.push(`raw ≥ ${rawReserveMin} after ×${atmosFactor} altitude correction`);
    if (config.flushingPulsationSystem && cr == null)
      reserveHintParts.push("enter working vacuum (1a) + milkline size for the cleaning reserve");
  } else {
    reserveHintParts.push("set the cluster count for the standard");
  }
  sections.push({
    key: "ReserveCharacteristics",
    title: "2 · Reserve",
    readings: [
      {
        key: "tr.effectiveReserve",
        label: "Effective reserve (2a)",
        unit: "L/min",
        hint: reserveHintParts.join(" · "),
        rule: rawReserveMin != null ? { kind: "atLeast", min: rawReserveMin } : { kind: "none" },
      },
      { key: "tr.reserveAirflow", label: "Airflow (2b)", unit: "L/min", rule: { kind: "none" } },
      { key: "tr.manualReserve", label: "Manual reserve (2c)", unit: "L/min", rule: { kind: "none" } },
      {
        key: "tr.regulationLoss",
        label: "Regulation loss (2d)",
        unit: "L/min",
        derived: "2c − 2a",
        // Manual p40 / ISO C.4.6: ≤ 10% of manual reserve or 35 L/min, whichever is greater.
        hint:
          manualReserve != null
            ? `≤ ${lossLimit(manualReserve)} (${lossPct}% of manual reserve, min ${lossFloor})`
            : `≤ ${lossPct}% of manual reserve or ${lossFloor}, whichever is greater`,
        rule: manualReserve != null ? { kind: "atMost", limit: lossLimit(manualReserve) } : { kind: "none" },
      },
      { key: "tr.regulatorLeakageAirflow", label: "Airflow at regulator leakage (2e)", unit: "L/min", rule: { kind: "none" } },
      {
        key: "tr.regulatorLeakage",
        label: "Regulator leakage (2f)",
        unit: "L/min",
        derived: "2e − 2b",
        // Manual p39/p41 / ISO C.4.8: ≤ 5% of manual reserve or 35 L/min, whichever is greater.
        hint:
          manualReserve != null
            ? `≤ ${leakLimit(manualReserve)} (${leakPct}% of manual reserve, min ${leakFloor})`
            : `≤ ${leakPct}% of manual reserve or ${leakFloor}, whichever is greater`,
        rule: manualReserve != null ? { kind: "atMost", limit: leakLimit(manualReserve) } : { kind: "none" },
      },
      // 2g/2h — the standards 2a is judged against, shown as figures the way legacy did so the
      // tester can quote them to the farmer. Calculated (derived.ts), no verdict of their own.
      {
        key: "tr.requiredEffectiveReserve",
        label: "Required effective reserve (2g)",
        unit: "L/min",
        derived: "manual p42 table, by cluster count",
        hint: erTable != null ? `for ${config.clusterCount} clusters` : "set the cluster count",
        rule: { kind: "none" },
      },
      ...(config.flushingPulsationSystem
        ? [
            {
              key: "tr.requiredCleaningReserve",
              label: "Required cleaning reserve (2h)",
              unit: "L/min",
              derived: "milkline size & 1a (manual p43)",
              hint: cr == null ? "enter working vacuum (1a) + milkline size" : "wash-injection system fitted",
              rule: { kind: "none" } as PassFailRule,
            },
          ]
        : []),
    ],
  });

  // 3 — Regulation characteristics
  sections.push({
    key: "RegulationCharacteristics",
    title: "3 · Regulation",
    readings: [
      { key: "tr.avgReceiverVacuum", label: "Average receiver vacuum (3a)", unit: "kPa", rule: { kind: "none" } },
      { key: "tr.minVacuumAirInlet", label: "Min vacuum at air inlet (3b)", unit: "kPa", rule: { kind: "none" } },
      { key: "tr.avgVacuumAirInlet", label: "Avg vacuum at air inlet (3c)", unit: "kPa", rule: { kind: "none" } },
      { key: "tr.maxVacuumIncrease", label: "Max vacuum increase (3d)", unit: "kPa", rule: { kind: "none" } },
      { key: "tr.avgVacuumStopAirInlet", label: "Avg vacuum, air inlet stopped (3e)", unit: "kPa", rule: { kind: "none" } },
      { key: "tr.fallOff", label: "Fall-off (3f)", unit: "kPa", derived: "3a − 3c", rule: ruleFor("tr.fallOff", { kind: "atMost", limit: 2 }) },
      { key: "tr.regulationUndershoot", label: "Regulation undershoot (3g)", unit: "kPa", derived: "3c − 3b", rule: ruleFor("tr.regulationUndershoot", { kind: "atMost", limit: 2 }) },
      { key: "tr.regulationOvershoot", label: "Regulation overshoot (3h)", unit: "kPa", derived: "3d − 3e", rule: ruleFor("tr.regulationOvershoot", { kind: "atMost", limit: 2 }) },
    ],
  });

  // 4 — Vacuum drop in airline
  sections.push({
    key: "VacuumDropAirline",
    title: "4 · Airline drop",
    readings: [
      { key: "tr.airlineVacReceiver", label: "Vacuum @ receiver (4a)", unit: "kPa", rule: { kind: "none" } },
      { key: "tr.airlineVacRegulator", label: "Vacuum @ regulator (4b)", unit: "kPa", rule: { kind: "none" } },
      // Manual p40 / ISO D.2.13: receiver→regulator drop ≤ 1 kPa.
      { key: "tr.airlineDropRR", label: "Drop receiver → regulator (4c)", unit: "kPa", derived: "4b − 4a", rule: ruleFor("tr.airlineDropRR", { kind: "atMost", limit: 1 }) },
      { key: "tr.airlineVacPump", label: "Vacuum @ pump (4d)", unit: "kPa", rule: { kind: "none" } },
      // Manual p40/p44 / ISO D.2.15: receiver→pump drop ≤ 3 kPa.
      { key: "tr.airlinePumpDrop", label: "Pump vacuum drop (4e)", unit: "kPa", derived: "4d − 4a", rule: ruleFor("tr.airlinePumpDrop", { kind: "atMost", limit: 3 }) },
    ],
  });

  // 5 — Regulator sensitivity. Manual p40 / ISO D.2.6: ≤ 1 kPa.
  sections.push({
    key: "RegulatorSensitivity",
    title: "5 · Regulator sensitivity",
    readings: [
      { key: "tr.regSensWorkingVac", label: "Working vacuum in milk system (5a)", unit: "kPa", rule: { kind: "none" } },
      { key: "tr.regulatorSensitivity", label: "Regulator sensitivity (5b)", unit: "kPa", derived: "5a − 1a", rule: ruleFor("tr.regulatorSensitivity", { kind: "atMost", limit: 1 }) },
    ],
  });

  // 6 — Reserve (vacuum off cluster) — optional
  sections.push({
    key: "ReserveVacuumOffCluster",
    title: "6 · Reserve off cluster",
    readings: [
      { key: "tr.reserveOffClusterInterceptor", label: "Vacuum at interceptor (6a)", unit: "kPa", rule: { kind: "none" } },
      { key: "tr.reserveOffClusterEffective", label: "Effective reserve off cluster (6b)", unit: "L/min", rule: { kind: "none" } },
    ],
  });

  // 7 — Vacuum gauge accuracy. Manual p40 / ISO D.2.3: farm gauge within ±1 kPa of test gauge.
  // One admin row ("tr.gaugeError") governs all three measurement points.
  const gaugeTol: PassFailRule = ruleFor("tr.gaugeError", { kind: "tolerance", target: 0, tolerance: 1 });
  sections.push({
    key: "VacuumGaugeAccuracy",
    title: "7 · Gauge accuracy",
    readings: [
      { key: "tr.farmGauge1", label: "Farm gauge — point 1 (7a)", unit: "kPa", rule: { kind: "none" } },
      { key: "tr.testGauge1", label: "Test gauge — point 1 (7b)", unit: "kPa", rule: { kind: "none" } },
      { key: "tr.gaugeError1", label: "Gauge error — point 1 (7c)", unit: "kPa", derived: "7a − 7b", hint: `± ${gaugeTol.tolerance} kPa`, rule: gaugeTol },
      { key: "tr.farmGauge2", label: "Farm gauge — point 2 (7d)", unit: "kPa", rule: { kind: "none" } },
      { key: "tr.testGauge2", label: "Test gauge — point 2 (7e)", unit: "kPa", rule: { kind: "none" } },
      { key: "tr.gaugeError2", label: "Gauge error — point 2 (7f)", unit: "kPa", derived: "7d − 7e", hint: `± ${gaugeTol.tolerance} kPa`, rule: gaugeTol },
      { key: "tr.farmGauge3", label: "Farm gauge — point 3 (7g)", unit: "kPa", rule: { kind: "none" } },
      { key: "tr.testGauge3", label: "Test gauge — point 3 (7h)", unit: "kPa", rule: { kind: "none" } },
      { key: "tr.gaugeError3", label: "Gauge error — point 3 (7i)", unit: "kPa", derived: "7g − 7h", hint: `± ${gaugeTol.tolerance} kPa`, rule: gaugeTol },
    ],
  });

  // 8 — Vacuum pump test. Capacity/speed limits are OEM-model lookups (manual pp8–30, 60), so
  // capture-only with the atmosphere-correction reminder; measured capacity × factor compares
  // against the OEM curve (manual p31 / ISO 5.3.2).
// When the pump's make and model are in the legacy catalogue its curve is known, so the spec is
  // worked out beside the tested figure (Jono, 15 Sep 2026: "put the spec there ... and then here
  // we're putting in what we tested"). It carries no verdict: see oemHint.
  const correction = atmosFactor !== 1 ? `× ${atmosFactor} altitude correction, then ` : "";
  const pumpHint = `${correction}compare to OEM curve`;
  const pumpReadings: ReadingDef[] = [];
  const pumps = Math.max(1, config.numberOfVacuumPumps);
  const pumpDetails = vacuumPumpRows(config);
  for (let i = 1; i <= pumps; i++) {
    const p = pumps > 1 ? ` — pump ${i}` : "";
    const detail = pumpDetails[i - 1];
    const oem = i <= MAX_OEM_PUMPS ? vacuumPumpFor(detail?.make, detail?.model) : null;
    pumpReadings.push({
      key: `tr.pumpCapacity${i}`,
      label: `Capacity @ 50 kPa (8a)${p}`,
      unit: "L/min",
      hint: oem ? `${correction}compare to the OEM capacity below` : pumpHint,
      rule: { kind: "none" },
    });
    if (oem) {
      pumpReadings.push({
        key: `tr.pumpOemCapacity${i}`,
        label: `OEM capacity at that speed${p}`,
        unit: "L/min",
        derived: OEM_CAPACITY_FORMULA,
        hint: oemHint(oem, readings[`tr.pumpMaxSpeed${i}`]),
        rule: { kind: "none" },
      });
    }
    pumpReadings.push(
      { key: `tr.pumpMinSpeed${i}`, label: `Minimum speed (8b)${p}`, unit: "rpm", rule: { kind: "none" } },
      { key: `tr.pumpMaxSpeed${i}`, label: `Maximum speed (8c)${p}`, unit: "rpm", hint: "@ 50 kPa", rule: { kind: "none" } },
    );
  }
  // 9b is legacy's "Air Flow Start: pump capacity at working vacuum" — the baseline the leakage
  // tests (10) are measured against — and 9a the exhaust pressure, an exception on the flowchart
  // taken only when the pump is out of spec (limit is OEM-specific; manual p24: Masport vane
  // ≤ 13 kPa). Both belong with the pump, so 8 and 9 are one section (tester feedback, 15 Sep 2026).
  pumpReadings.push(
    { key: "tr.pumpCapacityTotal", label: "Pump capacity at working vacuum — air flow start (9b)", unit: "L/min", hint: pumpHint, rule: { kind: "none" } },
    { key: "tr.exhaustPressure", label: "Exhaust pressure (9a)", unit: "kPa", hint: "only if the pump is out of spec · per manufacturer (Masport vane ≤ 13)", rule: { kind: "none" } },
  );
  sections.push({ key: "VacuumPumpTest", title: "8–9 · Vacuum pump(s)", readings: pumpReadings });

  return sections;
}

/** Airflow Tests (ISO 10–12) — leakage, ACR consumption (when fitted) and cluster air admission,
 * mirroring the resolver. Limits verified against manual p41–42 / ISO 6690 Annex C–D. */
export function airflowSections(
  config: MachineConfiguration,
  readings: Record<string, number> = {},
): ReadingSection[] {
  // Vacuum system leakage ≤ 5% of pump capacity (manual p41 / ISO C.5.4). Both the 10b value
  // (9b − 10a) and its limit come from 9b, the capacity at working vacuum — the per-pump 8a figures
  // are taken at 50 kPa, a different test point, so they are not a stand-in.
  const pumpCapacity: number | null = readings["tr.pumpCapacityTotal"] ?? null;
  // Cluster air admission is the machine TOTAL (10c − 12a, as legacy calculated it), judged at the
  // per-cluster band (manual p42 / ISO D.6: 4–12 L/min) × the cluster count — 37 clusters → 148–444.
  // Vented liners raise the ceiling to the manual's 35 per cluster (pp41–42) but keep the 4 per
  // cluster floor (Jono, 23 Sep 2026: the 4 litre minimum still applies). The admin standard stays
  // per cluster.
  const clusters = config.clusterCount;
  const ventedMax = paramFor("param.clusterAir.ventedMax", 35);
  const caaBand = ruleFor("add.clusterAirAdmission", { kind: "between", min: 4, max: 12 });
  const caaPerCluster: PassFailRule = config.linerVented
    ? { kind: "between", min: caaBand.kind === "between" || caaBand.kind === "atLeast" ? caaBand.min : 4, max: ventedMax }
    : caaBand;
  const caaRule: PassFailRule = clusters > 0 ? perUnitTotal(caaPerCluster, clusters) : { kind: "none" };
  const caaTotal = readings["add.clusterAirAdmission"];
  const caaHint =
    clusters > 0
      ? `${describeLimit(caaPerCluster)} per cluster × ${clusters} = ${describeLimit(caaRule)}` +
        (caaTotal != null ? ` · ${(caaTotal / clusters).toFixed(1)} per cluster` : "") +
        (config.linerVented ? " (vented liners)" : "")
      : "set the cluster count for the standard";
  const vacLeakPct = paramFor("param.vacLeak.pctOfPumpCapacity", 5);
  const vacLeakLimit = pumpCapacity != null ? Math.round((vacLeakPct / 100) * pumpCapacity) : null;
  // Milk system leakage ≤ 10 + 2 per cluster (manual p41 / ISO C.5.6).
  const milkLeakBase = paramFor("param.milkLeak.base", 10);
  const milkLeakPer = paramFor("param.milkLeak.perCluster", 2);
  const milkLeakLimit = config.clusterCount > 0 ? milkLeakBase + milkLeakPer * config.clusterCount : null;

  const sections: ReadingSection[] = [
    {
      key: "AirlineMilkSystemLeakage",
      title: "10 · Airline & milk leakage",
      readings: [
        { key: "add.airflowVacuumSystem", label: "Airflow — vacuum system (10a)", unit: "L/min", rule: { kind: "none" } },
        {
          key: "add.vacuumSystemLeakage",
          label: "Vacuum system leakage (10b)",
          unit: "L/min",
          derived: "9b − 10a",
          hint: vacLeakLimit != null ? `≤ ${vacLeakLimit} (${vacLeakPct}% of pump capacity)` : `≤ ${vacLeakPct}% of pump capacity — enter 9b first`,
          rule: vacLeakLimit != null ? { kind: "atMost", limit: vacLeakLimit } : { kind: "none" },
        },
        { key: "add.airflowMilkSystem", label: "Airflow — milk system (10c)", unit: "L/min", rule: { kind: "none" } },
        {
          key: "add.milkSystemLeakage",
          label: "Milk system leakage (10d)",
          unit: "L/min",
          derived: "10a − 10c",
          hint: milkLeakLimit != null ? `≤ ${milkLeakLimit} (${milkLeakBase} + ${milkLeakPer} per cluster)` : `≤ ${milkLeakBase} + ${milkLeakPer} per cluster`,
          rule: milkLeakLimit != null ? { kind: "atMost", limit: milkLeakLimit } : { kind: "none" },
        },
      ],
    },
  ];
  // ACR allowance: 7.5 L/min per unit, min 30, rounded up to 10s; doubled with bail-gate rams
  // (manual p41). Component count assumed = cluster count.
  const acrLimit = ancillaryAllowance(config.clusterCount, config.hasBailGates);
  if (config.hasAcr) {
    sections.push({ key: "AcrConsumption", title: "11 · ACR", readings: [
      { key: "add.acrAirflow", label: "ACR airflow (11a)", unit: "L/min", rule: { kind: "none" } },
      {
        key: "add.acrConsumption",
        label: "ACR consumption (11b)",
        unit: "L/min",
        derived: "10c − 11a",
        hint: acrLimit != null ? `≤ ${acrLimit} (7.5/unit, min 30${config.hasBailGates ? ", ×2 bail gates" : ""})` : undefined,
        rule: acrLimit != null ? { kind: "atMost", limit: acrLimit } : { kind: "none" },
      },
    ] });
  }
  sections.push({
    key: "ClusterAirAdmission",
    title: "12 · Cluster air admission",
    readings: [
      { key: "add.clusterAirAdmissionConnect", label: "Connected airflow (12a)", unit: "L/min", rule: { kind: "none" } },
      {
        key: "add.clusterAirAdmission",
        label: "Cluster air admission — total (12b)",
        unit: "L/min",
        derived: "10c − 12a",
        hint: caaHint,
        rule: caaRule,
      },
    ],
  });
  return sections;
}

/** Additional Tests — the unnumbered per-ancillary checks that follow the ISO flowchart (NZMPTA's
 * separate "Additional Tests" flowchart), gated by the machine's ancillaries, mirroring the
 * resolver. Limits verified against manual p41 / p61. */
export function additionalTestSections(
  config: MachineConfiguration,
  readings: Record<string, number> = {},
): ReadingSection[] {
  const sections: ReadingSection[] = [];
  // ACR / milk-meter allowance: 7.5 L/min per unit, min 30, rounded up to 10s; doubled with
  // bail-gate rams (manual p41). Component count assumed = cluster count.
  const acrLimit = ancillaryAllowance(config.clusterCount, config.hasBailGates);
  if (config.hasMilkMeters) {
    // Same 7.5/unit allowance family as ACRs (manual p41).
    sections.push({ key: "MilkMeter", title: "Milk meters", readings: [
      {
        key: "add.milkMeter",
        label: "Milk meter consumption",
        unit: "L/min",
        hint: acrLimit != null ? `≤ ${acrLimit} (7.5/unit, min 30${config.hasBailGates ? ", ×2 bail gates" : ""})` : undefined,
        rule: acrLimit != null ? { kind: "atMost", limit: acrLimit } : { kind: "none" },
      },
    ] });
  }
  // Teat sprays / stimulators / vacuum-operated gates: 10 L/min per cluster (manual p41).
  const perClusterAllowance = paramFor("param.perCluster.tenLpm", 10);
  const perClusterTen = config.clusterCount > 0 ? perClusterAllowance * config.clusterCount : null;
  if (config.hasTeatSprayer) {
    sections.push({ key: "TeatSpray", title: "Teat sprayer", readings: [
      {
        key: "add.teatSpray",
        label: "Teat sprayer consumption",
        unit: "L/min",
        hint: perClusterTen != null ? `≤ ${perClusterTen} (${perClusterAllowance} per cluster)` : undefined,
        rule: perClusterTen != null ? { kind: "atMost", limit: perClusterTen } : { kind: "none" },
      },
    ] });
  }
  if (config.hasBailGates || config.hasBackingGate) {
    sections.push({ key: "GateCylinder", title: "Gates", readings: [
      {
        key: "add.gateCylinder",
        label: "Gate cylinder consumption",
        unit: "L/min",
        hint: perClusterTen != null ? `≤ ${perClusterTen} (vacuum-operated gates, ${perClusterAllowance} per cluster)` : undefined,
        rule: perClusterTen != null ? { kind: "atMost", limit: perClusterTen } : { kind: "none" },
      },
    ] });
  }
  if (config.hasReleaserPump) {
    // Minimum speed/power by cluster count × heads from the legacy MinSpeedPowerCal table — the
    // check the legacy app recorded as SpeedO/PowerO ticks. The table covers 6–40 clusters; a
    // plant outside it (or an un-entered head count) stays capture-only, as it was in legacy.
    const heads = readings["add.releaserHeads"];
    const releaserReq = heads != null ? releaserRequirement(config.clusterCount, heads) : null;
    const releaserFor = (what: "minSpeed" | "power", unit: string) =>
      releaserReq != null
        ? `≥ ${releaserReq[what]} ${unit} (${config.clusterCount} clusters, ${heads} head${heads === 1 ? "" : "s"})`
        : heads == null
          ? "enter the number of heads for the standard"
          : "no standard for this cluster count / head count";
    sections.push({ key: "ReleaserPumpHeads", title: "Releaser pump", readings: [
      { key: "add.releaserHeads", label: "Number of heads", unit: "", rule: { kind: "none" } },
      {
        key: "add.releaserSpeed",
        label: "Releaser pump speed",
        unit: "rpm",
        hint: releaserFor("minSpeed", "rpm"),
        rule: releaserReq != null ? { kind: "atLeast", min: releaserReq.minSpeed } : { kind: "none" },
      },
      {
        key: "add.releaserPower",
        label: "Releaser pump power",
        unit: "kW",
        hint: releaserFor("power", "kW"),
        rule: releaserReq != null ? { kind: "atLeast", min: releaserReq.power } : { kind: "none" },
      },
    ] });
  }
  sections.push({ key: "RegulatorLoad", title: "Peak regulator load", readings: [
    { key: "add.regulatorLoad", label: "Peak regulator load", unit: "kPa", rule: ruleFor("add.regulatorLoad", { kind: "atMost", limit: 2 }) },
  ] });
  return sections;
}

/** Pulsator step readings that aren't per-pulsator — ISO 14 (pulsator & ancillary air consumption),
 * 15 (test pulsation) and airline stability. The per-pulsator rates/ratios are captured in the
 * pulsator row table (see pulsatorStats). */
export function pulsatorSections(
  config: MachineConfiguration,
  readings: Record<string, number> = {},
): ReadingSection[] {
  const workingVacuum = readings["tr.workingVacuum"];
  // Pulsator consumption is at most 35 L/min per CLUSTER, not per pulsator, with no minimum: a
  // 30-bale shed on 15 pulsators is allowed 35 × 30 (Jono, 23 Sep 2026 - a DeLaval EP100 uses about
  // all of it, a Waikato Smartpulse as little as 10). It replaces the manual p41 "30 per 10 units"
  // reading, which failed the 790 L/min legacy passed for 19 units on 37 clusters. The admin
  // standard is per cluster, like 12b.
  const clusters = config.clusterCount;
  const consumptionPerCluster = ruleFor("puls.pulsatorConsumption", { kind: "atMost", limit: 35 });
  const consumptionRule: PassFailRule = clusters > 0 ? perUnitTotal(consumptionPerCluster, clusters) : { kind: "none" };
  const consumption = readings["puls.pulsatorConsumption"];
  const consumptionHint =
    clusters > 0
      ? `${describeLimit(consumptionPerCluster)} per cluster × ${clusters} = ${describeLimit(consumptionRule)}` +
        (consumption != null ? ` · ${(consumption / clusters).toFixed(1)} per cluster` : "")
      : "set the cluster count for the standard";
  // Manual p40 / ISO D.2.17: chamber vacuum within 2 kPa of the working vacuum — judged on the
  // drop (15b = 1a − 15a), which is where legacy recorded the verdict.
  const chamberDelta = paramFor("param.chamberVac.maxDelta", 2);
  // The spread limits come from the param.pulsation.* rows; a puls.*Spread rule row, if an admin
  // ever adds one, takes over — so the hint is built from the resolved rule, never the raw param.
  const limits = pulsationLimits();
  const rateSpreadRule = ruleFor("puls.rateSpread", { kind: "atMost", limit: limits.rateSpreadMax });
  const ratioSpreadRule = ruleFor("puls.ratioSpread", { kind: "atMost", limit: limits.ratioSpreadMax });
  return [
    {
      key: "PulsatorAncillary",
      title: "14 · Pulsator & ancillary",
      readings: [
        { key: "puls.airflowMilkSystem", label: "Airflow — milk system (14a)", unit: "L/min", rule: { kind: "none" } },
        { key: "puls.milkSystemAncillary", label: "Milk-system ancillary consumption (14b)", unit: "L/min", derived: "12a − 14a", rule: { kind: "none" } },
        { key: "puls.airflowPulsators", label: "Airflow — pulsators (14c)", unit: "L/min", rule: { kind: "none" } },
        {
          key: "puls.pulsatorConsumption",
          label: "Pulsator consumption (14d)",
          unit: "L/min",
          derived: "14a − 14c",
          hint: consumptionHint,
          rule: consumptionRule,
        },
        { key: "puls.airflowVacuumSystem", label: "Airflow — vacuum system (14e)", unit: "L/min", rule: { kind: "none" } },
        { key: "puls.vacuumSystemAncillary", label: "Vacuum-system ancillary consumption (14f)", unit: "L/min", derived: "14c − 14e", rule: { kind: "none" } },
      ],
    },
    {
      key: "TestPulsation",
      title: "15 · Test pulsation",
      readings: [
        {
          key: "puls.maxChamberVacuum",
          label: "Max pulsation chamber vacuum, B phase (15a)",
          unit: "kPa",
          hint: "lowest maximum across the pulsators tested — judged on the drop (15b)",
          rule: { kind: "none" },
        },
        {
          key: "puls.testPulsationReading",
          label: "Pulsator airline drop (15b)",
          unit: "kPa",
          derived: "1a − 15a",
          hint: `≤ ${chamberDelta} kPa below working vacuum${workingVacuum != null ? ` (${workingVacuum})` : " — enter 1a first"}`,
          rule: { kind: "atMost", limit: chamberDelta },
        },
        // The machine-level extremes off the analyser — legacy's "Rate Range Fastest / Slowest"
        // and "Ratio Range Highest / Lowest". The spread checks (manual pp49–53 / ISO Table D.5)
        // are judged on these, not on the faulty-pulsator rows, which are only a subset. The ratio
        // pair is pooled across front and back quarters, as legacy captured it: the analyser reads
        // two channels at a time, so the tester enters the highest and lowest whichever side they
        // were on (Jono, 23 Sep 2026, asked about four per-group figures and chose to keep two).
        { key: "puls.rateFastest", label: "Fastest pulsator rate", unit: "ppm", hint: "from the analyser, all units", rule: { kind: "none" } },
        { key: "puls.rateSlowest", label: "Slowest pulsator rate", unit: "ppm", rule: { kind: "none" } },
        {
          key: "puls.rateSpread",
          label: "Rate spread",
          unit: "ppm",
          derived: "fastest − slowest",
          hint: `≤ ${rateSpreadRule.limit} ppm between pulsators`,
          rule: rateSpreadRule,
        },
        { key: "puls.ratioHighest", label: "Highest pulsator ratio", unit: "%", hint: "from the analyser, all units, front or back", rule: { kind: "none" } },
        { key: "puls.ratioLowest", label: "Lowest pulsator ratio", unit: "%", rule: { kind: "none" } },
        {
          key: "puls.ratioSpread",
          label: "Ratio spread",
          unit: "%",
          derived: "highest − lowest",
          hint: `≤ ${ratioSpreadRule.limit}% between pulsators`,
          rule: ratioSpreadRule,
        },
      ],
    },
    {
      key: "PulsatorStability",
      title: "Stability",
      readings: [
        // Manual p40: pulsator airline vacuum dips must not exceed 4 kPa.
        { key: "puls.airlineStability", label: "Pulsator airline stability", unit: "kPa", rule: ruleFor("puls.airlineStability", { kind: "atMost", limit: 4 }) },
      ],
    },
  ];
}

/** Individual Cluster Tests (optional) — ISO 13 / Table D.6 per-cluster limits: total air
 * admission ≤ 12 (vented liners ≤ 35 per manual pp41–42), leakage ≤ 2, air-vent admission ≥ 4. */
export function individualClusterSections(config: MachineConfiguration): ReadingSection[] {
  const ventedMax = paramFor("param.clusterAir.ventedMax", 35);
  const totalRule = config.linerVented
    ? ({ kind: "atMost", limit: ventedMax } satisfies PassFailRule)
    : ruleFor("ica.totalAirAdmission", { kind: "atMost", limit: 12 });
  const leakRule = ruleFor("ica.leakage", { kind: "atMost", limit: 2 });
  const ventRule = ruleFor("ica.airVentAdmission", { kind: "atLeast", min: 4 });
  return [
    {
      key: "IndividualCluster",
      title: "13 · Individual cluster",
      readings: [
        {
          key: "ica.totalAirAdmission",
          label: "Total cluster air admission (13a)",
          unit: "L/min",
          hint: config.linerVented ? `≤ ${ventedMax} (vented liners)` : `≤ ${totalRule.limit}`,
          rule: totalRule,
        },
        { key: "ica.leakage", label: "Cluster leakage (13b)", unit: "L/min", hint: `≤ ${leakRule.limit}`, rule: leakRule },
        { key: "ica.airVentAdmission", label: "Air-vent admission (13c)", unit: "L/min", hint: `≥ ${ventRule.min}`, rule: ventRule },
      ],
    },
  ];
}

/** All numerical reading sections across the reading-based steps — used to build the fault list.
 * Pass the test's readings so the cross-reading standards (manual-reserve %, working-vacuum
 * derived limits, pump-capacity %) evaluate with their real thresholds. */
export function allReadingSections(
  config: MachineConfiguration,
  readings: Record<string, number> = {},
): ReadingSection[] {
  // Flowchart order: vacuum → airflow → individual cluster → pulsation → additional.
  return [
    ...testRecordSections(config, readings),
    ...airflowSections(config, readings),
    ...individualClusterSections(config),
    ...pulsatorSections(config, readings),
    ...additionalTestSections(config, readings),
  ];
}
