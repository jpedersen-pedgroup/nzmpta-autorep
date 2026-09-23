// Test Summary report (M4) — generated entirely on-device from the LocalTest so it works
// offline. pdfmake (+ Roboto fonts) loads as a lazy chunk only when a report is requested.
//
// Layout (tester feedback F19): page one is what the farmer acts on — the MPNZ letterhead, farm
// and test details, the fault summary with its recommendations, and the tester's comments. The
// working (configuration, numbers, per-unit tables, visual checks) starts on page two under a
// compact running header, and the amendment history closes the document as its own page.
import type { Content, CustomTableLayout, TDocumentDefinitions, TableCell } from "pdfmake/interfaces";
import type { LocalTest } from "../db/testStore";
import type { FaultSeverity } from "../wizard/types";
import { loadPdfLib, loadPdfMake } from "./generatorChunks";
import { aggregate } from "../faults/faultAggregator";
import { buildFaultInputs } from "../faults/buildFaults";
import { allReadingSections } from "../passfail/standards";
import { evaluate, type PassFailRule } from "../passfail/passFail";
import { preStartSections, runningSectionsFor } from "../wizard/visualChecklist";
import { resolveWizard } from "../wizard/wizardStepResolver";
import { pulsatorSummary } from "../passfail/pulsatorStats";
import { getPrivacyContent } from "../config/privacyContent";
import { formatDisplayDate, type CalibrationDates } from "../calibration/status";
import { getCachedCalibration } from "../sync/calibrationSync";
import { getCachedCompanyBranding, type CompanyBranding } from "../sync/companyBrandingSync";
import { PLANT_LABELS, PUMP_LUBRICATION_LABELS } from "../wizard/configLabels";
import { recordedRows } from "../ui/measurementRows";
import { isBlankPumpRow, releaserPumpRows, vacuumPumpRows } from "../wizard/pumpRows";
import { proposedNextTestDate } from "../wizard/nextTestDate";
import {
  BRAND_DARK,
  BRAND_LIGHT,
  LOGO_LOCKUP_SVG,
  LOGO_MARK_SVG,
  PAGE_WIDTH,
  footerSwirlSvg,
  letterheadSwirlSvg,
  ruleSwooshSvg,
} from "./brandArt";

const BRAND = BRAND_DARK;
const INK = "#1e293b";
const MUTED = "#64748b";
const RULE = "#dbe2ee";
const PANEL = "#f1f4fa";
const HEAD_FILL = "#e6ecf6";
const PASS = "#15803d";
const FAIL = "#dc2626";
const FAIL_ROW = "#fdf1f1";

const SEVERITY_STYLE: Record<FaultSeverity, { ink: string; fill: string }> = {
  Critical: { ink: "#991b1b", fill: "#fde2e2" },
  Major: { ink: "#9a3412", fill: "#feead7" },
  Minor: { ink: "#475569", fill: "#e5e9f0" },
};

// Page geometry, in points. The top margin leaves room for the running header on pages 2+; page
// one fills the same band with the letterhead.
const MARGIN_X = 40;
const MARGIN_TOP = 64;
const MARGIN_BOTTOM = 56;
const CONTENT_WIDTH = PAGE_WIDTH - MARGIN_X * 2;
// Where the letterhead swirl sits on page one, and how far down the content must start to clear it.
const LETTERHEAD_LIFT = 30;
const SWIRL_TOP = 20 - LETTERHEAD_LIFT;
const LETTERHEAD_CLEARANCE = 64;

function describeRule(rule: PassFailRule, unit: string): string {
  switch (rule.kind) {
    case "atMost": return `≤ ${rule.limit} ${unit}`;
    case "atLeast": return `≥ ${rule.min} ${unit}`;
    case "between": return `${rule.min}–${rule.max} ${unit}`;
    case "tolerance": return `± ${rule.tolerance} ${unit}`;
    default: return "—";
  }
}

// The report is an NZ compliance document: every date on it is New Zealand time regardless of
// how the generating device is configured (the admin portal does the same — see PR #53).
const NZ_TIME_ZONE = "Pacific/Auckland";

function fmtDate(iso?: string | null): string {
  if (!iso) return "—";
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? String(iso) : d.toLocaleString("en-NZ", { timeZone: NZ_TIME_ZONE });
}

/** The day alone, spelled out — "15 September 2026" — for the letterhead. */
function fmtDay(iso?: string | null): string {
  if (!iso) return "—";
  const d = new Date(iso);
  return Number.isNaN(d.getTime())
    ? String(iso)
    : d.toLocaleDateString("en-NZ", { day: "numeric", month: "long", year: "numeric", timeZone: NZ_TIME_ZONE });
}

/** A stored calendar day (yyyy-mm-dd) spelled out — "15 September 2027". Built from the parts, not
 * through Date, so no time zone can shift it by a day. */
function fmtCalendarDay(ymd: string): string {
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(ymd);
  if (!m) return ymd;
  const month = new Date(Date.UTC(2000, Number(m[2]) - 1, 1)).toLocaleString("en-NZ", { month: "long", timeZone: "UTC" });
  return `${Number(m[3])} ${month} ${m[1]}`;
}

/** A calibration expiry as dd/mm/yyyy — the stamped snapshot, else the tester's live profile. */
function calDate(stamped?: string | null, fallback?: string | null): string {
  const iso = stamped ?? fallback;
  return iso ? formatDisplayDate(iso) : "—";
}

const th = (text: string): TableCell => ({ text, bold: true, fontSize: 7.5, color: BRAND });

/** The table style every grid on the report shares: tinted header row, hairline rules between
 * rows, no verticals. */
const GRID: CustomTableLayout = {
  hLineWidth: (i, node) => (i === 0 || i === node.table.body.length ? 0 : 0.5),
  vLineWidth: () => 0,
  hLineColor: () => RULE,
  paddingLeft: () => 5,
  paddingRight: () => 5,
  paddingTop: () => 3.5,
  paddingBottom: () => 3.5,
  fillColor: (row) => (row === 0 ? HEAD_FILL : null),
};

/** A borderless table used as a tinted panel. */
const PANEL_LAYOUT: CustomTableLayout = {
  hLineWidth: () => 0,
  vLineWidth: () => 0,
  paddingLeft: () => 10,
  paddingRight: () => 10,
  paddingTop: () => 8,
  paddingBottom: () => 6,
};

/** The bundled Roboto has no arrow glyph, so "Drop receiver → regulator" printed a blank box.
 * Spell it out wherever a table cell carries one. */
const spellArrows = (s: string) => s.replace(/\s*→\s*/g, " to ");

function printable(cell: TableCell): TableCell {
  if (typeof cell === "string") return spellArrows(cell);
  const c = cell as { text?: unknown };
  return typeof c.text === "string" ? ({ ...c, text: spellArrows(c.text) } as TableCell) : cell;
}

function grid(widths: (string | number)[], body: TableCell[][], margin?: [number, number, number, number]): Content {
  return { table: { headerRows: 1, widths, body: body.map((row) => row.map(printable)) }, layout: GRID, fontSize: 9, color: INK, margin } as Content;
}

/** Motor size as the tester typed it - a bare number is kW, anything else stands as written. */
function motorText(v?: string | null): string {
  const s = (v ?? "").trim();
  if (s === "") return "—";
  return /^\d+(\.\d+)?$/.test(s) ? `${s} kW` : s;
}

/** A section heading: brand-blue title over a short dark bar that runs into a light rule.
 * headlineLevel lets pageBreakBefore keep it off the foot of a page. */
function sectionHeader(text: string, pageBreak = false): Content {
  return {
    stack: [
      { text, fontSize: 12.5, bold: true, color: BRAND, headlineLevel: 1 },
      {
        canvas: [
          { type: "rect", x: 0, y: 3, w: 30, h: 2.4, color: BRAND },
          { type: "rect", x: 30, y: 3.6, w: CONTENT_WIDTH - 30, h: 1.2, color: BRAND_LIGHT },
        ],
      },
    ],
    margin: [0, 16, 0, 6],
    ...(pageBreak ? { pageBreak: "before" } : {}),
  } as Content;
}

function subHeader(text: string): Content {
  return { text, fontSize: 9.5, bold: true, color: INK, margin: [0, 8, 0, 3], headlineLevel: 2 } as Content;
}

/** A small caps label over a value, for the detail panels. */
function field(label: string, value: string | null | undefined, bold = false): Content {
  return {
    stack: [
      { text: label.toUpperCase(), fontSize: 6.5, color: MUTED, characterSpacing: 0.6 },
      { text: value?.trim() || "—", fontSize: 9, bold, color: INK, margin: [0, 1, 0, 0] },
    ],
    margin: [0, 0, 0, 7],
  } as Content;
}

function panel(stack: Content[], fill = PANEL): Content {
  return { table: { widths: ["*"], body: [[{ stack, fillColor: fill }]] }, layout: PANEL_LAYOUT } as Content;
}

/** A panel with a coloured bar down its left edge — the result banner and the comments box. */
function barPanel(stack: Content[], bar: string, fill: string, margin?: [number, number, number, number]): Content {
  return {
    table: { widths: [3, "*"], body: [[{ text: "", fillColor: bar }, { stack, fillColor: fill }]] },
    layout: { ...PANEL_LAYOUT, paddingLeft: (i: number) => (i === 0 ? 0 : 10), paddingRight: (i: number) => (i === 0 ? 0 : 10) },
    margin,
  } as Content;
}

/** The testing company that carried out the test, for the letterhead. The logo is a data URL
 * (`data:image/png;base64,…`), as the admin portal stores it. */
export interface ReportBranding {
  companyName?: string | null;
  companyLogo?: string | null;
}

/** The name the company logo is registered under in the document's `images` dictionary. pdfmake
 * embeds an inline data-URL image afresh every time it lays one out, so a logo drawn on every page
 * (and re-measured when a heading is pushed to the next page) was going into the PDF seven times
 * over; a named image is embedded once. */
const COMPANY_LOGO_IMAGE = "companyLogo";

/** The raster logo for the `images` dictionary, when the upload is one pdfmake can embed. */
/** Judged on the image bytes, not the declared type: pdfmake throws on anything that isn't really
 * a PNG or JPEG, which fails the whole report, and a logo migrated from the legacy system can
 * carry the wrong label. The base64 of the PNG signature starts "iVBORw0KGgo"; of a JPEG, "/9j/". */
function rasterLogo(dataUrl: string | null | undefined): string | null {
  const payload = /^data:image\/[\w.+-]+;base64,(.*)$/s.exec(dataUrl ?? "")?.[1];
  return payload && (payload.startsWith("iVBORw0KGgo") || payload.startsWith("/9j/")) ? dataUrl! : null;
}

/** The markup of an SVG logo, or null. Decoded as UTF-8, so a macron in the artwork's text
 * survives. */
function svgLogo(dataUrl: string | null | undefined): string | null {
  const m = /^data:image\/svg\+xml(;base64)?,(.*)$/s.exec(dataUrl ?? "");
  if (!m) return null;
  try {
    const svg = m[1]
      ? new TextDecoder().decode(Uint8Array.from(atob(m[2]), (c) => c.charCodeAt(0)))
      : decodeURIComponent(m[2]);
    return svg.includes("<svg") ? svg : null;
  } catch {
    return null; // malformed base64 or escapes
  }
}

/** A logo from a data URL, fitted inside `fit`. pdfmake draws PNG and JPEG as images (by name —
 * see COMPANY_LOGO_IMAGE) and SVG as vectors; anything else (GIF, WebP, a mislabelled file) is
 * left off rather than breaking the report. Uploads are restricted to those three formats
 * (Services/LogoImage.cs), so this only matters for older data. */
function logoNode(dataUrl: string | null | undefined, fit: [number, number]): Content | null {
  // Both places a logo goes sit against the right margin.
  if (rasterLogo(dataUrl)) return { image: COMPANY_LOGO_IMAGE, fit, alignment: "right" } as Content;
  const svg = svgLogo(dataUrl);
  return svg ? ({ svg, fit, alignment: "right" } as Content) : null;
}

function severityCell(severity: FaultSeverity): TableCell {
  const s = SEVERITY_STYLE[severity] ?? SEVERITY_STYLE.Major;
  return { text: severity.toUpperCase(), fontSize: 7, bold: true, color: s.ink, fillColor: s.fill, alignment: "center", characterSpacing: 0.4 };
}

/** Builds the pdfmake document definition for the Test Summary. Pure — unit-testable.
 * `calibrationFallback` is the tester's live profile calibration, used only for a test that
 * hasn't been stamped yet (a report previewed before sign-off); a completed test always
 * reprints its own stamped snapshot. `branding` is the testing company shown beside the MPNZ
 * mark; without it the letterhead carries MPNZ alone. */
export function buildTestSummaryDoc(
  test: LocalTest,
  calibrationFallback?: CalibrationDates,
  branding?: ReportBranding,
): TDocumentDefinitions {
  const config = test.config;
  const summary = aggregate(buildFaultInputs(test));
  const version = test.version ?? 1;
  // Migrated tests carry faults/recommendations + verdicts as recorded; reprint those faithfully
  // rather than recomputing against today's standards.
  const isLegacy = test.recordedRecommendations !== undefined;
  const farm = test.farm;
  const farmName = farm?.name ?? test.farmName ?? "—";
  const plant = `${PLANT_LABELS[config.plantType] ?? config.plantType}${config.plantSize ? ` · ${config.plantSize}` : ""}`;

  // --- Amendment history -----------------------------------------------------------------------
  // A re-edited test carries its cumulative amendment chain; render it as the report's final
  // page so every change made after the original sign-off is visible on the printed record.
  const amendmentBlock: Content[] = buildAmendmentBlock(test);

  // --- Letterhead ------------------------------------------------------------------------------
  // The MPNZ lockup on the left and the testing company's logo, when there is one, on the right;
  // the title sits under the swirl so the layout is the same with or without a company logo.
  const companyLogo = logoNode(branding?.companyLogo, [170, 56]);
  const companyRaster = rasterLogo(branding?.companyLogo);
  const letterhead: Content = {
    columns: [
      { svg: LOGO_LOCKUP_SVG, width: 176 },
      { width: "*", text: "" },
      ...(companyLogo ? [{ width: "auto", stack: [companyLogo], margin: [0, 2, 0, 0] } as Content] : []),
    ],
    margin: [0, -LETTERHEAD_LIFT, 0, LETTERHEAD_CLEARANCE],
  };
  const titleLine: Content = {
    columns: [
      { width: "*", text: "Milking Machine Test Summary", fontSize: 17, bold: true, color: BRAND },
      {
        width: "auto",
        alignment: "right",
        margin: [0, 5, 0, 0],
        text: test.markedCompleteAt
          ? [
              { text: `Tested ${fmtDay(test.markedCompleteAt)}`, color: INK },
              ...(version > 1 ? [{ text: `   Version ${version}`, bold: true, color: BRAND }] : []),
            ]
          : [{ text: "DRAFT — not yet signed off", bold: true, color: SEVERITY_STYLE.Major.ink }],
        fontSize: 9.5,
      },
    ],
    margin: [0, 0, 0, 10],
  };

  // --- Farm + test details -----------------------------------------------------------------------
  const address = [farm?.addressLine1, farm?.addressLine2, farm?.town, farm?.postCode].filter(Boolean).join(", ");
  const farmPanel = panel([
    { text: "FARM", fontSize: 7, bold: true, color: BRAND, characterSpacing: 1.2, margin: [0, 0, 0, 3] },
    { text: farmName, fontSize: 13, bold: true, color: INK },
    { text: address || "—", fontSize: 8.5, color: MUTED, margin: [0, 1, 0, 8] },
    {
      columns: [
        { width: "*", stack: [field("Supply number", farm?.supplyNumber), field("Milk company", farm?.milkCompanyName), field("Farmer", farm?.farmerName)] },
        { width: "*", stack: [field("Region", farm?.regionName), field("RAPID number", farm?.rapidNumber), field("Phone", farm?.contactPhone)] },
      ],
      columnGap: 10,
    },
  ]);
  // The next test date is what the farmer most needs from this panel, so it leads it, larger. A
  // signed-off test prints what was recorded (nothing for one signed off before the date existed,
  // rather than inventing one); a draft prints what sign-off would record, marked as proposed.
  const nextTestDate = test.markedCompleteAt
    ? test.nextTestDate ?? null
    : proposedNextTestDate(test, new Date().toISOString());
  const nextTestBlock: Content[] = nextTestDate
    ? [
        { text: "NEXT TEST DUE", fontSize: 6.5, color: MUTED, characterSpacing: 0.6 },
        {
          text: [
            { text: fmtCalendarDay(nextTestDate), fontSize: 12, bold: true, color: BRAND },
            ...(test.markedCompleteAt ? [] : [{ text: "  proposed", fontSize: 8, color: MUTED }]),
          ],
          margin: [0, 1, 0, 8],
        } as Content,
      ]
    : [];
  const testPanel = panel([
    { text: "TEST", fontSize: 7, bold: true, color: BRAND, characterSpacing: 1.2, margin: [0, 0, 0, 3] },
    ...nextTestBlock,
    field("Completed", test.markedCompleteAt ? fmtDate(test.markedCompleteAt) : "Not yet signed off", true),
    ...(branding?.companyName?.trim() ? [field("Tested by", branding.companyName)] : []),
    field("Machine", `${plant} · ${config.clusterCount || "—"} clusters`),
    { text: "CALIBRATION EXPIRY", fontSize: 6.5, color: MUTED, characterSpacing: 0.6 },
    {
      table: {
        widths: ["auto", "*"],
        body: [
          ["Airflow meter", calDate(test.calAirFlowMeters, calibrationFallback?.airFlowMeters)],
          ["Pulsation tester", calDate(test.calPulsatorTesters, calibrationFallback?.pulsatorTesters)],
          ["Vacuum gauge", calDate(test.calVacuumGauges, calibrationFallback?.vacuumGauges)],
        ].map(([k, v]) => [{ text: k, color: MUTED }, { text: v, color: INK }]),
      },
      layout: { hLineWidth: () => 0, vLineWidth: () => 0, paddingLeft: () => 0, paddingRight: () => 8, paddingTop: () => 1, paddingBottom: () => 1 },
      fontSize: 8.5,
      margin: [0, 2, 0, 4],
    } as Content,
  ]);
  const detailsBlock: Content = { columns: [{ width: "58%", stack: [farmPanel] }, { width: "*", stack: [testPanel] }], columnGap: 10 };

  const versionNotice: Content[] =
    version > 1
      ? [
          barPanel(
            [{
              text: `Version ${version} — supersedes an earlier completed test${
                amendmentBlock.length > 0 ? " (all changes are listed in the Amendment history section)" : ""
              }`,
              fontSize: 8.5, color: BRAND,
            }],
            BRAND, PANEL, [0, 10, 0, 0],
          ),
        ]
      : [];

  // --- Result banner + fault summary -------------------------------------------------------------
  const plural = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`;
  const countChips: Content[] = (["Critical", "Major", "Minor"] as FaultSeverity[]).map((sev) => {
    const n = sev === "Critical" ? summary.critical : sev === "Major" ? summary.major : summary.minor;
    const s = SEVERITY_STYLE[sev];
    return {
      width: "auto",
      table: { body: [[{ text: `${n}  ${sev.toUpperCase()}`, fontSize: 7.5, bold: true, color: n ? s.ink : MUTED, fillColor: n ? s.fill : "#ffffff", characterSpacing: 0.4 }]] },
      layout: { hLineWidth: () => 0, vLineWidth: () => 0, paddingLeft: () => 6, paddingRight: () => 6, paddingTop: () => 2, paddingBottom: () => 2 },
    } as Content;
  });
  const worst: FaultSeverity = summary.critical ? "Critical" : summary.major ? "Major" : "Minor";
  const resultBanner: Content[] = isLegacy
    ? []
    : summary.total === 0
      ? [barPanel(
          [
            { text: "No faults found", fontSize: 12, bold: true, color: PASS },
            { text: "No faults recorded — the machine passed every completed check.", fontSize: 9, color: INK, margin: [0, 2, 0, 2] },
          ],
          PASS, "#eef8f1", [0, 12, 0, 0],
        )]
      : [barPanel(
          [
            {
              columns: [
                {
                  width: "*",
                  stack: [
                    { text: `${plural(summary.total, "fault needs", "faults need")} attention`, fontSize: 12, bold: true, color: SEVERITY_STYLE[worst].ink },
                    { text: "Each fault is listed below with the recommended action.", fontSize: 8.5, color: MUTED, margin: [0, 2, 0, 0] },
                  ],
                },
                { width: "auto", columns: countChips, columnGap: 4, margin: [0, 4, 0, 0] },
              ],
            },
          ],
          SEVERITY_STYLE[worst].ink, "#fdf6f2", [0, 12, 0, 0],
        )];

  const faultRows: TableCell[][] = [[th("Severity"), th("Area"), th("Fault"), th("Recommendation")]];
  for (const g of summary.groups) {
    for (const f of g.faults) {
      faultRows.push([
        severityCell(f.severity),
        { text: g.component, color: MUTED },
        { text: f.description },
        { text: f.recommendation ?? "" },
      ]);
    }
  }
  const faultBlock: Content[] = isLegacy
    ? recordedFaultBlock(test)
    : summary.total === 0
      ? []
      : [grid([52, 78, "*", "*"], faultRows)];

  // General comments sit under the fault table: what the tester wants the farmer to know that no
  // fault line carries. Migrated tests print theirs inside recordedFaultBlock.
  const notes = test.notes?.trim();
  const notesBlock: Content[] =
    !isLegacy && notes
      ? [
          barPanel(
            [
              { text: "General comments", fontSize: 9.5, bold: true, color: BRAND, margin: [0, 0, 0, 3] },
              { text: notes, fontSize: 9, color: INK, lineHeight: 1.2 },
            ],
            BRAND_LIGHT, PANEL, [0, 12, 0, 0],
          ),
        ]
      : [];

  // --- Machine configuration (page 2 onward) ----------------------------------------------------
  const flags: string[] = [];
  if (config.vsdFitted) flags.push("VSD");
  if (config.hasAcr) flags.push("ACRs");
  if (config.hasMilkMeters) flags.push("Milk meters");
  if (config.hasTeatSprayer) flags.push("Teat sprayer");
  if (config.hasBailGates) flags.push("Bail gates");
  if (config.hasBackingGate) flags.push("Backing gate");
  if (config.hasReleaserPump) flags.push("Releaser pump");
  if (config.linerVented) flags.push("Vented liners");
  if (config.flushingPulsationSystem) flags.push("Flushing pulsation");
  if (!config.isoPortsAvailable) flags.push("No ISO ports (short test)");

  const k = (text: string): TableCell => ({ text, bold: true, fontSize: 7.5, color: BRAND, fillColor: PANEL });
  const configBlock: Content = {
    table: {
      widths: [78, "*", 78, "*"],
      body: [
        [k("Plant"), plant, k("Clusters"), String(config.clusterCount || "—")],
        [k("Pulsators"), `${config.pulsatorCount || "—"} · ${config.pulsatorBrand ?? "—"} ${config.pulsatorModel ?? ""}`.trim(), k("Configuration"), config.pulsatorConfiguration ?? "—"],
        [k("Shell"), config.shellModel ?? "—", k("Claw"), config.clawModel ?? "—"],
        [k("Liners (F/B)"), `${config.linerModel ?? "—"} / ${config.backLiner ?? "—"}`, k("Milkline"), config.milklineSize ? `${config.milklineSize} mm` : "—"],
        [k("Vacuum pumps"), `${config.numberOfVacuumPumps} · ${PUMP_LUBRICATION_LABELS[config.pumpLubrication] ?? config.pumpLubrication}`, k("Atmos. pressure"), config.atmosPressureSeaLevel ? `${config.atmosPressureSeaLevel} kPa` : "—"],
        [k("Equipment"), { text: flags.join(", ") || "—", colSpan: 3 }, "", ""],
      ],
    },
    layout: { ...GRID, hLineWidth: (i, node) => (i === 0 || i === node.table.body.length ? 0 : 0.5), fillColor: () => null },
    fontSize: 9,
    color: INK,
  };

  // Pump details - make/model/motor/regulator per pump, so whoever quotes a replacement has the
  // nameplate without going back to the plant (tester feedback, 15 Sep 2026). Blank rows are left
  // off, so a test that never filled them in prints no table at all.
  const pumpBlock: Content[] = [];
  const vacuumRows: TableCell[][] = [
    [th("Vacuum pump"), th("Make / model"), th("Motor"), th("Regulator"), th("Drives milk pump")],
  ];
  vacuumPumpRows(config).forEach((p, i) => {
    if (isBlankPumpRow(p)) return;
    vacuumRows.push([
      `Pump ${i + 1}`,
      `${p.make ?? ""} ${p.model ?? ""}`.trim() || "—",
      motorText(p.motorSize),
      p.regulatorType ?? "—",
      p.drivesMilkPump ? "Yes" : "No",
    ]);
  });
  if (vacuumRows.length > 1) pumpBlock.push(grid(["auto", "*", "auto", "*", "auto"], vacuumRows, [0, 10, 0, 0]));
  const releaserRows: TableCell[][] = [[th("Releaser pump"), th("Make / model"), th("Motor")]];
  releaserPumpRows(config).forEach((p, i) => {
    if (isBlankPumpRow(p)) return;
    releaserRows.push([`Releaser ${i + 1}`, `${p.make ?? ""} ${p.model ?? ""}`.trim() || "—", motorText(p.motorSize)]);
  });
  if (releaserRows.length > 1) pumpBlock.push(grid(["auto", "*", "auto"], releaserRows, [0, 10, 0, 0]));

  // --- Numerical readings ----------------------------------------------------------------------
  const readingBlocks: Content[] = [];
  for (const sec of allReadingSections(config, test.readings)) {
    const entered = sec.readings.filter((r) => test.readings[r.key] != null);
    if (entered.length === 0) continue;
    const body: TableCell[][] = [[th("Reading"), th("Value"), th("Standard"), th("Result")]];
    for (const r of entered) {
      const v = test.readings[r.key];
      // As-recorded verdict for migrated tests; recompute for live ones.
      const verdict = test.verdicts?.[r.key] ?? evaluate(v, r.rule);
      const fill = verdict === "fail" ? FAIL_ROW : undefined;
      body.push([
        { text: r.label, fillColor: fill },
        { text: `${v} ${r.unit}`, fillColor: fill },
        { text: describeRule(r.rule, r.unit), color: MUTED, fillColor: fill },
        verdict === "noStandard"
          ? { text: "—", color: MUTED, fillColor: fill }
          : { text: verdict.toUpperCase(), color: verdict === "pass" ? PASS : FAIL, bold: true, fontSize: 8, fillColor: fill },
      ]);
    }
    readingBlocks.push(subHeader(sec.title));
    readingBlocks.push(grid(["*", 70, 80, 44], body));
  }

  // --- Per-unit rows ---------------------------------------------------------------------------
  const unitBlocks: Content[] = [];
  const pulsatorRows = recordedRows(test.pulsatorRows);
  if (pulsatorRows.length) {
    const s = pulsatorSummary(pulsatorRows, test.config.pulsatorModel);
    const body: TableCell[][] = [
      [th("Unit no."), th("Rate (ppm)"), th("Ratio F (%)"), th("Ratio B (%)"), th("Phase b (%)"), th("Phase d (ms)"), th("Max vac (kPa)"), th("Limp (%)")],
      ...pulsatorRows.map((r) => [
        { text: r.unit, bold: true } as TableCell,
        ...["rate", "ratioFront", "ratioBack", "phaseB", "phaseDms", "maxVacuum", "limp"].map((key) => r.values[key] ?? ""),
      ]),
    ];
    // Neutral on the report: tests captured before "Enter all" was removed can carry a row for
    // every unit, and those were never all faulty.
    unitBlocks.push(sectionHeader("Pulsator results"));
    unitBlocks.push(grid(["*", "*", "*", "*", "*", "*", "*", "*"], body));
    // The rate/ratio spread is judged on the analyser's machine-level extremes and printed with
    // the numerical results; the recorded units are a subset, so only the model-band checks on
    // them are printed here.
    const bandParts: Content[] = [];
    if (s.rateBand && s.rateBandOk != null) {
      bandParts.push(`Model rate band ${s.rateBand.min}–${s.rateBand.max} ppm`, {
        text: s.rateBandOk ? " PASS" : " FAIL", color: s.rateBandOk ? PASS : FAIL, bold: true,
      });
    }
    if (s.ratioBand && s.ratioBandOk != null) {
      bandParts.push(`${bandParts.length ? "   ·   " : ""}Model ratio band ${s.ratioBand.min}–${s.ratioBand.max}%`, {
        text: s.ratioBandOk ? " PASS" : " FAIL", color: s.ratioBandOk ? PASS : FAIL, bold: true,
      });
    }
    if (bandParts.length) unitBlocks.push({ text: bandParts, fontSize: 9, color: INK, margin: [0, 5, 0, 0] });
  }
  const clusterRows = recordedRows(test.clusterRows);
  if (clusterRows.length) {
    const body: TableCell[][] = [
      [th("Cluster no."), th("Total air admission"), th("Leakage"), th("Air-vent admission")],
      ...clusterRows.map((r) => [
        { text: r.unit, bold: true } as TableCell,
        ...["totalAirAdmission", "leakage", "airVent"].map((key) => r.values[key] ?? ""),
      ]),
    ];
    unitBlocks.push(sectionHeader("Individual cluster tests"));
    unitBlocks.push(grid([70, "*", "*", "*"], body));
  }

  // --- Visual checks ---------------------------------------------------------------------------
  const runningKeys = resolveWizard(config).steps.find((s) => s.step === "VisualFaultsRunning")?.sections ?? [];
  const visualSections = [...preStartSections(config.hasReleaserPump), ...runningSectionsFor(runningKeys)];
  let okCount = 0;
  const visualFaultRows: TableCell[][] = [[th("Area"), th("Check"), th("Fault"), th("Severity")]];
  for (const sec of visualSections) {
    for (const it of sec.items) {
      const e = test.visualFaults[it.key];
      if (e?.status === "ok") okCount++;
      if (e?.status === "fault") {
        visualFaultRows.push([
          { text: sec.title, color: MUTED },
          it.label,
          e.observation ?? e.note ?? "—",
          severityCell(e.severity ?? "Major"),
        ]);
      }
    }
  }
  const visualBlock: Content[] = [
    {
      text: [
        { text: `${okCount}`, bold: true, color: PASS }, " item(s) verified OK   ·   ",
        { text: `${visualFaultRows.length - 1}`, bold: true, color: visualFaultRows.length > 1 ? FAIL : PASS }, " fault(s) logged",
      ],
      fontSize: 9.5, color: INK, margin: [0, 0, 0, 5],
    },
  ];
  if (visualFaultRows.length > 1) visualBlock.push(grid([90, "*", "*", 52], visualFaultRows));
  // Recorded measurements and choices — tube type, lengths, diameters, which way the clusters
  // come on — so whoever replaces a part knows what to buy (tester feedback, 15 Sep 2026). The
  // label carries the unit where there is one.
  const measureRows: TableCell[][] = [[th("Area"), th("Item"), th("Recorded")]];
  for (const sec of visualSections) {
    for (const it of sec.items) {
      if (!it.data && !it.choice) continue;
      const v = (test.dataFields?.[it.key] ?? "").trim();
      if (!v) continue;
      measureRows.push([{ text: sec.title, color: MUTED }, it.label, { text: v, bold: true }]);
    }
  }
  if (measureRows.length > 1) {
    visualBlock.push(subHeader("Recorded measurements"));
    visualBlock.push(grid([90, "*", "auto"], measureRows));
  }

  // --- Attestations ---------------------------------------------------------------------------
  const attestRows = test.attestations.map((a) => ({
    text: `${fmtDate(a.attestedAt)} — ${a.step}${a.section ? ` · ${a.section}` : ""}: "${a.text}"`,
    fontSize: 8,
    color: MUTED,
    margin: [0, 1, 0, 0] as [number, number, number, number],
  }));

  // --- Attachment note -------------------------------------------------------------------------
  const attachmentBlock: Content[] = test.pulsationPdf
    ? [
        sectionHeader("Attachments"),
        {
          text: `Pulsation analyser report: ${test.pulsationPdf.name} (attached ${fmtDate(test.pulsationPdf.attachedAt)}) — appended to this document.`,
          fontSize: 9,
          color: MUTED,
        },
      ]
    : [];

  const generated = new Date().toLocaleString("en-NZ", { timeZone: NZ_TIME_ZONE });

  return {
    pageSize: "A4",
    pageMargins: [MARGIN_X, MARGIN_TOP, MARGIN_X, MARGIN_BOTTOM],
    info: { title: `Test Summary — ${farmName}` },
    defaultStyle: { color: INK },
    ...(companyRaster ? { images: { [COMPANY_LOGO_IMAGE]: companyRaster } } : {}),
    // Page one carries the letterhead swirl and a flourish in the bottom corner; later pages
    // are left plain so the tables read cleanly.
    background: (page, size) =>
      page === 1
        ? [
            { svg: letterheadSwirlSvg(), width: PAGE_WIDTH, absolutePosition: { x: 0, y: SWIRL_TOP } },
            { svg: footerSwirlSvg(), width: 220, absolutePosition: { x: size.width - 220, y: size.height - 60 } },
          ]
        : null,
    // Pages 2+: the MPNZ mark, the report and farm name, the company logo when there is one, and
    // the lockup's tapered rule underneath. A fresh node per page: pdfmake lays out what it's given.
    header: (page) =>
      page === 1
        ? null
        : {
            margin: [MARGIN_X, 22, MARGIN_X, 0],
            stack: [
              {
                columns: [
                  { svg: LOGO_MARK_SVG, width: 62 },
                  {
                    width: "*",
                    alignment: "right",
                    margin: [0, 7, 0, 0],
                    text: [
                      { text: "Milking Machine Test Summary", bold: true, color: BRAND },
                      { text: `   ${farmName}${farm?.supplyNumber ? ` · Supply ${farm.supplyNumber}` : ""}`, color: MUTED },
                    ],
                    fontSize: 8,
                  },
                  ...((): Content[] => {
                    const logo = logoNode(branding?.companyLogo, [84, 22]);
                    return logo ? [{ width: "auto", stack: [logo], margin: [12, 0, 0, 0] } as Content] : [];
                  })(),
                ],
              },
              { svg: ruleSwooshSvg(CONTENT_WIDTH), width: CONTENT_WIDTH, margin: [0, 5, 0, 0] },
            ],
          },
    footer: (page, pages) => {
      const privacyFooter = getPrivacyContent().reportFooterText;
      return {
        margin: [MARGIN_X, 14, MARGIN_X, 0],
        stack: [
          {
            columns: [
              { text: `NZMPTA AutoRep · generated ${generated}`, fontSize: 7, color: MUTED },
              // Page one's number sits clear of the corner flourish.
              { text: `Page ${page} of ${pages}`, alignment: page === 1 ? "left" : "right", fontSize: 7, color: MUTED, width: page === 1 ? 180 : "*" },
            ],
          },
          ...(privacyFooter
            ? [{ text: privacyFooter, fontSize: 6, color: MUTED, margin: [0, 2, page === 1 ? 170 : 0, 0] } as Content]
            : []),
        ],
      };
    },
    // Keep a heading with what follows it: one that would land in the last stretch of a page
    // starts the next page instead (a "Visual checks" heading was stranded at a page foot).
    pageBreakBefore: (node) => {
      const at = node.startPosition?.verticalRatio ?? 0;
      return (node.headlineLevel === 1 && at > 0.86) || (node.headlineLevel === 2 && at > 0.92);
    },
    content: [
      // ---- Page one: what the farmer acts on ----
      letterhead,
      titleLine,
      detailsBlock,
      ...versionNotice,
      ...resultBanner,
      // A clean machine says so in the banner; an empty heading under it would read as missing.
      ...(faultBlock.length > 0 ? [sectionHeader("Fault summary & recommendations"), ...faultBlock] : []),
      ...notesBlock,
      // ---- Page two onward: the working ----
      sectionHeader("Machine configuration", true),
      configBlock,
      ...pumpBlock,
      sectionHeader("Numerical test results"),
      ...(readingBlocks.length > 0 ? readingBlocks : [{ text: "No readings entered.", fontSize: 9, color: MUTED } as Content]),
      ...unitBlocks,
      // Migrated tests show their recorded faults in the Fault Summary above; the recomputed
      // visual-checks section (driven by the empty visualFaults map) is omitted for them.
      ...(isLegacy ? [] : [sectionHeader("Visual checks"), ...visualBlock]),
      ...attachmentBlock,
      ...(attestRows.length > 0 ? [sectionHeader("Attestations"), ...attestRows] : []),
      ...amendmentBlock,
    ],
  };
}

// The Amendment history page: one block per superseding version (ascending), each a table of
// Section / Field / Previous / Amended. Starts on its own page — it's the audit appendix.
function buildAmendmentBlock(test: LocalTest): Content[] {
  const amendments = [...(test.amendments ?? [])].sort((a, b) => a.version - b.version);
  if (amendments.length === 0) return [];

  const out: Content[] = [
    { ...(sectionHeader("Amendment history") as object), pageBreak: "before" } as Content,
    {
      text: "This test has been amended since it was first completed. Each version below lists every recorded change against the version it replaced. Earlier versions remain on record.",
      fontSize: 9, color: MUTED, margin: [0, 0, 0, 6],
    },
  ];

  for (const a of amendments) {
    const supersedes = `supersedes version ${a.baseVersion}${
      a.baseCompletedAt ? ` (completed ${fmtDate(a.baseCompletedAt)})` : ""
    }`;
    out.push({
      text: `Version ${a.version} — completed ${fmtDate(a.amendedAt)}${a.amendedBy ? ` by ${a.amendedBy}` : ""} · ${supersedes}`,
      fontSize: 9.5, bold: true, color: INK, margin: [0, 8, 0, 3], headlineLevel: 2,
    });

    if (a.baseUnavailable) {
      out.push({
        text: "The superseded version was not available on the signing device, so a field-level comparison could not be recorded.",
        fontSize: 9, color: FAIL,
      });
      continue;
    }
    if (a.changes.length === 0) {
      out.push({ text: "Re-completed with no data changes.", fontSize: 9, color: MUTED });
      continue;
    }

    const body: TableCell[][] = [
      [th("Section"), th("Field"), th("Previous"), th("Amended")],
      ...a.changes.map((c) => [
        { text: c.section, fontSize: 9, color: MUTED } as TableCell,
        { text: c.label, fontSize: 9 } as TableCell,
        { text: c.from, fontSize: 9, color: MUTED } as TableCell,
        { text: c.to, fontSize: 9, bold: true } as TableCell,
      ]),
    ];
    out.push(grid(["auto", "*", "*", "*"], body));
  }
  return out;
}

// Fault Summary block for a migrated test: recorded faults + section recommendations + comment,
// exactly as recorded (no recompute).
function recordedFaultBlock(test: LocalTest): Content[] {
  const recs = test.recordedRecommendations ?? [];
  const faults = test.recordedVisualFaults ?? [];
  const comment = test.notes?.trim();
  if (faults.length === 0 && recs.length === 0 && !comment) {
    return [{ text: "No faults or recommendations were recorded for this test.", color: PASS, fontSize: 10, margin: [0, 4, 0, 0] }];
  }
  const out: Content[] = [];
  if (faults.length > 0) {
    out.push(subHeader("Recorded faults"));
    out.push({ ul: faults, fontSize: 9, color: INK, markerColor: BRAND });
  }
  for (const r of recs) {
    out.push(subHeader(r.label));
    out.push({ text: r.text, fontSize: 9 });
  }
  if (comment) {
    out.push(subHeader("General comments"));
    out.push({ text: comment, fontSize: 9 });
  }
  return out;
}

/** The yyyy-mm-dd for the report filename: the New Zealand date the test was signed off, not the
 * UTC date inside the timestamp. A test completed at 8:41 am NZST on the 15th is 20:41Z on the
 * 14th, and the file was being named for the 14th. The zone parameter exists for tests. */
export function reportDateStamp(iso: string, timeZone: string = NZ_TIME_ZONE): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso.slice(0, 10);
  // en-CA is the locale whose numeric date form is yyyy-mm-dd.
  return new Intl.DateTimeFormat("en-CA", { year: "numeric", month: "2-digit", day: "2-digit", timeZone }).format(d);
}

function base64ToBytes(b64: string): Uint8Array {
  const bin = atob(b64);
  const bytes = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
  return bytes;
}

function downloadBlob(bytes: Uint8Array, filename: string): void {
  const blob = new Blob([bytes as BlobPart], { type: "application/pdf" });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  a.click();
  setTimeout(() => URL.revokeObjectURL(url), 10_000);
}

interface CreatedPdf {
  download(filename: string): void;
  getBuffer(cb?: (b: Uint8Array) => void): Promise<Uint8Array> | void;
}

/** pdfmake 0.3 returns a Promise from getBuffer; 0.2 used a callback — support both. */
function pdfBuffer(created: CreatedPdf): Promise<Uint8Array> {
  return new Promise((resolve, reject) => {
    try {
      const result = created.getBuffer((b) => resolve(b));
      if (result && typeof (result as Promise<Uint8Array>).then === "function") {
        (result as Promise<Uint8Array>).then(resolve, reject);
      }
    } catch (err) {
      reject(err);
    }
  });
}

/** Which company's letterhead a report generated on this device carries, given the tester's
 * cached company. A test stamped at sign-off keeps its own company: the cached logo only when the
 * tester is still with that company, otherwise its name alone (the device holds just the current
 * company's logo, and printing it on an old employer's test would misattribute the work). An
 * unstamped test (a draft preview, or one signed off before the stamp existed) takes the tester's
 * current company. */
export function brandingForTest(test: LocalTest, current: CompanyBranding | null): ReportBranding | undefined {
  if (test.testingCompanyId) {
    if (current?.id === test.testingCompanyId) return { companyName: current.name, companyLogo: current.logo };
    return test.testingCompanyName ? { companyName: test.testingCompanyName } : undefined;
  }
  return current ? { companyName: current.name, companyLogo: current.logo } : undefined;
}

/** Generates and downloads the PDF; the attached pulsation analyser report (if any) is appended
 * page-for-page. pdfmake, the fonts and pdf-lib all load as lazy chunks on first use.
 * `branding` is given by the read-only server view (the company the test was done for); on the
 * tester's own device it is resolved from the cached company. */
export async function downloadTestSummaryPdf(test: LocalTest, branding?: ReportBranding): Promise<void> {
  const { pdfMake, vfs } = await loadPdfMake();
  // pdfmake 0.3.x: register the Roboto virtual file system.
  (pdfMake as { addVirtualFileSystem(v: unknown): void }).addVirtualFileSystem(vfs);

  const name = `Test Summary - ${(test.farm?.name ?? test.farmName ?? "farm").replace(/[^\w\- ]+/g, "")} - ${
    reportDateStamp(test.markedCompleteAt ?? test.updatedAt)
  }.pdf`;
  // A report previewed before sign-off has no stamped calibration yet — fall back to the
  // tester's current profile so the preview matches what sign-off will record.
  const calibration = test.markedCompleteAt ? undefined : await getCachedCalibration().catch(() => undefined);
  const letterhead =
    branding ?? brandingForTest(test, await getCachedCompanyBranding().catch(() => null));
  const created = (pdfMake as { createPdf(doc: TDocumentDefinitions): CreatedPdf }).createPdf(
    buildTestSummaryDoc(test, calibration, letterhead),
  );

  if (test.pulsationPdf) {
    try {
      const { PDFDocument } = await loadPdfLib();
      const summaryDoc = await PDFDocument.load(await pdfBuffer(created));
      const attachDoc = await PDFDocument.load(base64ToBytes(test.pulsationPdf.base64));
      const pages = await summaryDoc.copyPages(attachDoc, attachDoc.getPageIndices());
      for (const page of pages) summaryDoc.addPage(page);
      downloadBlob(await summaryDoc.save(), name);
      return;
    } catch {
      // Unreadable/encrypted attachment — deliver the summary alone rather than nothing.
      const { showToast } = await import("../ui/toast");
      showToast("The attached PDF could not be appended — downloaded the summary without it.", "error");
    }
  }
  created.download(name);
}
