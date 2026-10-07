// Test Summary report (M4) — generated entirely on-device from the LocalTest so it works
// offline. pdfmake (+ Roboto fonts) loads as a lazy chunk only when a report is requested.
//
// Layout (tester feedback F19): page one is what the farmer acts on — the MPNZ letterhead, farm
// and test details, the fault summary with its recommendations, and the tester's comments. The
// working (configuration, then each family of tests with its per-unit table, visual checks) starts
// on page two under a compact running header — straight after the summary, when that runs over
// onto page two itself — and the amendment history closes the document as its own page.
//
// Each of those is a part the tester can print on its own (see REPORT_PARTS) — the legacy app
// printed its seven reports one by one, and a tester still often wants only some of them.
import type { Content, CustomTableLayout, TDocumentDefinitions, TableCell } from "pdfmake/interfaces";
import type { LocalTest, TesterDetails } from "../db/testStore";
import type { FaultSeverity } from "../wizard/types";
import { loadPdfLib, loadPdfMake } from "./generatorChunks";
import { aggregate } from "../faults/faultAggregator";
import { buildFaultInputs } from "../faults/buildFaults";
import {
  additionalTestSections,
  airflowSections,
  individualClusterSections,
  pulsatorSections,
  testRecordSections,
  type ReadingSection,
} from "../passfail/standards";
import { evaluate, type PassFailRule } from "../passfail/passFail";
import { preStartSections, runningSectionsFor } from "../wizard/visualChecklist";
import { resolveWizard } from "../wizard/wizardStepResolver";
import { pulsatorSummary } from "../passfail/pulsatorStats";
import { getPrivacyContent } from "../config/privacyContent";
import { formatDisplayDate, type CalibrationDates } from "../calibration/status";
import { getCachedCalibration } from "../sync/calibrationSync";
import { getCachedCompanyBranding, type CompanyBranding } from "../sync/companyBrandingSync";
import { getCachedTesterDetails } from "../sync/testerDetailsSync";
import { attachmentBase64 } from "../sync/pulsationAttachment";
import { PLANT_LABELS, PUMP_LUBRICATION_LABELS } from "../wizard/configLabels";
import { recordedRows } from "../ui/measurementRows";
import { isBlankPumpRow, isBlankRegulatorRow, regulatorRows, releaserPumpRows, vacuumPumpRows } from "../wizard/pumpRows";
import { nzDate, proposedNextTestDate } from "../wizard/nextTestDate";
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

/** The mandatory Compliance Disclaimer (PRD story 43), printed on page one of every report. The
 * wording is fixed by the signed-off Requirements & Scope v1.1, section 7.3: change it only when
 * NZMPTA changes that text. */
export const COMPLIANCE_DISCLAIMER =
  "This Machine Test may identify numerous hazards, however it in no way guarantees safety compliance " +
  "for all or any hazard/s. It is the farm owner’s responsibility to ensure that all hazards comply " +
  "with WorkSafe and relevant NZ Safety Standard/s.";

/** The reminder that sits under the next test date on page one. */
export const ANNUAL_TEST_NOTE =
  "Milking machines should be fully tested at least once a year by an NZMPTA Registered Milking Machine Tester.";

/** What the general comments box says when the tester left none. */
export const NO_GENERAL_COMMENTS = "No general comments were recorded for this test.";

/** The copyright line, for the year the report is generated (a New Zealand year). */
export function copyrightNotice(year: number): string {
  return `© ${year} New Zealand Milking and Pumping Trade Association. All rights reserved.`;
}

/** The parts of the report, in print order. The full report is every part that has something in
 * it; the sign-off step's section picker prints any selection of them. The legacy app's seven
 * reports are all here — the numbers split by wizard step, as the rebuilt wizard captures them —
 * plus the audit trail and the attached analyser report, whose pages go on the end. */
export const REPORT_PARTS = [
  "summary",
  "machine",
  "vacuum",
  "airflow",
  "cluster",
  "pulsation",
  "additional",
  "visual",
  "analyser",
  "audit",
] as const;
export type ReportPart = (typeof REPORT_PARTS)[number];

/** Each working part's heading on the report, which the section picker calls it too. The test
 * families carry their wizard steps' names. */
const PART_HEADINGS = {
  machine: "Machine configuration",
  vacuum: "Vacuum tests (ISO 1–9)",
  airflow: "Airflow tests (ISO 10–12)",
  cluster: "Individual cluster tests (ISO 13)",
  pulsation: "Pulsation & ancillary (ISO 14–15)",
  additional: "Additional tests",
  visual: "Visual checks",
} as const satisfies Partial<Record<ReportPart, string>>;

/** A part as the section picker lists it. */
export interface ReportPartOption {
  part: ReportPart;
  label: string;
  /** A second line, where the name alone doesn't say what prints. */
  hint?: string;
}

const SEVERITY_STYLE: Record<FaultSeverity, { ink: string; fill: string }> = {
  Critical: { ink: "#991b1b", fill: "#fde2e2" },
  Major: { ink: "#9a3412", fill: "#feead7" },
  Minor: { ink: "#475569", fill: "#e5e9f0" },
};

// Page geometry, in points. The top margin leaves room for the running header on pages 2+; page
// one fills the same band with the letterhead.
const MARGIN_X = 40;
const MARGIN_TOP = 64;
// The bottom margin holds the footer plus, on page one, the compliance disclaimer anchored just
// above it. pdfmake has one set of margins for every page, so the band is reserved on all of them:
// that's what guarantees page one's content can never run into the disclaimer.
const FOOTER_HEIGHT = 56;
const DISCLAIMER_BAND = 34;
const MARGIN_BOTTOM = FOOTER_HEIGHT + DISCLAIMER_BAND;
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
function sectionHeader(text: string): Content {
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
  } as Content;
}

/** The node, starting a new page. */
function startsPage(node: Content): Content {
  return { ...(node as object), pageBreak: "before" } as Content;
}

/** Marks the heading the working starts at, after the summary. pageBreakBefore starts page two
 * with it — unless the summary has already run onto page two (a long fault list, or comments that
 * didn't fit), when it carries straight on under the summary instead of leaving that page all but
 * empty. A static page break can't tell the two apart. */
const WORKING_STARTS = "workingStarts";

function startsWorking(node: Content): Content {
  return { ...(node as object), id: WORKING_STARTS } as Content;
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

/** "Tested by" in the Test panel: who did the test and how to reach them, as the farmer needs it
 * — name, company, phone, and NZMPTA registration number with its expiry. Each line only when known;
 * nothing at all when neither a tester nor a company is known. */
function testedByBlock(tester: TesterDetails | null, companyName?: string | null): Content[] {
  const company = companyName?.trim();
  if (!tester && !company) return [];
  const reg = tester?.registrationNumber
    ? `NZMPTA registration ${tester.registrationNumber}${tester.registrationExpiry ? ` · expires ${formatDisplayDate(tester.registrationExpiry)}` : ""}`
    : null;
  const lines: Content[] = [
    ...(tester ? [{ text: tester.name, fontSize: 9, bold: true, color: INK, margin: [0, 1, 0, 0] } as Content] : []),
    ...(company ? [{ text: company, fontSize: 8.5, color: INK, margin: [0, tester ? 0 : 1, 0, 0] } as Content] : []),
    ...(tester?.phone ? [{ text: `Phone ${tester.phone}`, fontSize: 8.5, color: INK } as Content] : []),
    ...(reg ? [{ text: reg, fontSize: 7.5, color: MUTED } as Content] : []),
  ];
  return [{ stack: [{ text: "TESTED BY", fontSize: 6.5, color: MUTED, characterSpacing: 0.6 }, ...lines], margin: [0, 0, 0, 7] } as Content];
}

/** What each rating means, for the legend under the fault table (the association's rating scheme,
 * as the legacy report printed it). */
export const SEVERITY_MEANING: Record<FaultSeverity, string> = {
  Critical: "Negatively affects milk quality or animal health and welfare, or is a serious risk to milker health and safety.",
  Major: "Affects animal comfort, may cause a breakdown, affects the ability to harvest milk efficiently, or compromises milker safety.",
  Minor: "Has the potential to affect milk quality, animal health and welfare, or milker safety in the future.",
};

/** The severity legend: each rating's pill beside what it means, kept whole on one page. */
function severityLegend(): Content {
  return {
    stack: [
      { text: "SEVERITY RATINGS", fontSize: 6.5, bold: true, color: MUTED, characterSpacing: 0.8, margin: [0, 0, 0, 3] },
      {
        table: {
          widths: [52, "*"],
          body: (["Critical", "Major", "Minor"] as FaultSeverity[]).map((sev) => [
            severityCell(sev),
            { text: SEVERITY_MEANING[sev], fontSize: 7.5, color: INK },
          ]),
        },
        layout: {
          hLineWidth: () => 0, vLineWidth: () => 0,
          paddingLeft: (i: number) => (i === 0 ? 0 : 8), paddingRight: () => 0, paddingTop: () => 1.5, paddingBottom: () => 1.5,
        },
      },
    ],
    unbreakable: true,
    margin: [0, 8, 0, 0],
  } as Content;
}

function severityCell(severity: FaultSeverity): TableCell {
  const s = SEVERITY_STYLE[severity] ?? SEVERITY_STYLE.Major;
  return { text: severity.toUpperCase(), fontSize: 7, bold: true, color: s.ink, fillColor: s.fill, alignment: "center", characterSpacing: 0.4 };
}

/** The compliance disclaimer, boxed at the foot of page one of every report (migrated ones and
 * partial prints included), in the band the bottom margin reserves for it above the footer — so
 * it's always on page one, however long the fault list, and never collides with the content. */
function disclaimerAt(pageHeight: number): Content {
  const pad = 7;
  return {
    table: {
      widths: [CONTENT_WIDTH - pad * 2 - 1.2],
      body: [[{
        text: [
          { text: "COMPLIANCE DISCLAIMER   ", fontSize: 6, bold: true, color: MUTED, characterSpacing: 0.6 },
          { text: COMPLIANCE_DISCLAIMER, fontSize: 6.8, color: INK },
        ],
        lineHeight: 1.1,
      }]],
    },
    layout: {
      hLineWidth: () => 0.6, vLineWidth: () => 0.6, hLineColor: () => RULE, vLineColor: () => RULE,
      paddingLeft: () => pad, paddingRight: () => pad, paddingTop: () => 4, paddingBottom: () => 4,
    },
    absolutePosition: { x: MARGIN_X, y: pageHeight - MARGIN_BOTTOM + 4 },
  } as Content;
}

/** Every part of the report for this test, each headed and ready to print. A part with nothing
 * in it for this test — no airflow readings, say, or no analyser report attached — is empty. The
 * arguments are buildTestSummaryDoc's. */
function reportParts(
  test: LocalTest,
  calibrationFallback?: CalibrationDates,
  branding?: ReportBranding,
  testerFallback?: TesterDetails | null,
): Record<ReportPart, Content[]> {
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
  // rather than inventing one); a draft prints what sign-off would record, marked as proposed. A
  // draft amendment already carries the original test's date, which is settled, not proposed.
  const nextTestProposed = !test.markedCompleteAt && !test.supersedesId;
  const nextTestDate = nextTestProposed
    ? proposedNextTestDate(test, new Date().toISOString())
    : test.nextTestDate ?? null;
  const nextTestBlock: Content[] = nextTestDate
    ? [
        { text: "NEXT TEST DUE", fontSize: 6.5, color: MUTED, characterSpacing: 0.6 },
        {
          text: [
            { text: fmtCalendarDay(nextTestDate), fontSize: 12, bold: true, color: BRAND },
            ...(nextTestProposed ? [{ text: "  proposed", fontSize: 8, color: MUTED }] : []),
          ],
          margin: [0, 1, 0, 2],
        } as Content,
      ]
    : [];
  const annualNote: Content = { text: ANNUAL_TEST_NOTE, fontSize: 7, color: MUTED, lineHeight: 1.1, margin: [0, 0, 0, 8] };
  const testPanel = panel([
    { text: "TEST", fontSize: 7, bold: true, color: BRAND, characterSpacing: 1.2, margin: [0, 0, 0, 3] },
    ...nextTestBlock,
    annualNote,
    field("Completed", test.markedCompleteAt ? fmtDate(test.markedCompleteAt) : "Not yet signed off", true),
    ...testedByBlock(test.testedBy ?? testerFallback ?? null, branding?.companyName),
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
      : [grid([52, 78, "*", "*"], faultRows), severityLegend()];

  // General comments sit under the fault table: what the tester wants the farmer to know that no
  // fault line carries. The box prints on every report, migrated ones included, and says so when
  // there are none: a missing box reads as though the comments had dropped off the report.
  const notes = test.notes?.trim();
  const notesBlock: Content = barPanel(
    [
      { text: "General comments", fontSize: 9.5, bold: true, color: BRAND, margin: [0, 0, 0, 3] },
      notes
        ? { text: notes, fontSize: 9, color: INK, lineHeight: 1.2 }
        : { text: NO_GENERAL_COMMENTS, fontSize: 9, color: MUTED },
    ],
    BRAND_LIGHT, PANEL, [0, 12, 0, 0],
  );

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

  // Pump details - make/model/motor per pump, so whoever quotes a replacement has the
  // nameplate without going back to the plant (tester feedback, 15 Sep 2026). Blank rows are left
  // off, so a test that never filled them in prints no table at all.
  const pumpBlock: Content[] = [];
  const vacuumRows: TableCell[][] = [
    [th("Vacuum pump"), th("Make / model"), th("Motor"), th("Drives milk pump")],
  ];
  vacuumPumpRows(config).forEach((p, i) => {
    if (isBlankPumpRow(p)) return;
    vacuumRows.push([
      `Pump ${i + 1}`,
      `${p.make ?? ""} ${p.model ?? ""}`.trim() || "—",
      motorText(p.motorSize),
      p.drivesMilkPump ? "Yes" : "No",
    ]);
  });
  if (vacuumRows.length > 1) pumpBlock.push(grid(["auto", "*", "auto", "auto"], vacuumRows, [0, 10, 0, 0]));
  // Regulators by type and count, then the tester's call on whether they suit the plant - printed
  // only when answered, with a No in red so it isn't missed.
  const regulatorBody: TableCell[][] = [[th("Regulator"), th("Type"), th("Qty")]];
  regulatorRows(config).forEach((r, i) => {
    if (isBlankRegulatorRow(r)) return;
    regulatorBody.push([`Regulator ${i + 1}`, r.type?.trim() || "—", r.quantity != null ? String(r.quantity) : "—"]);
  });
  if (config.regulatorsSuitable != null) {
    regulatorBody.push([
      { text: "Correct and big enough for this plant", colSpan: 2 },
      "",
      config.regulatorsSuitable
        ? { text: "Yes", color: PASS, bold: true }
        : { text: "No", color: FAIL, bold: true, fillColor: FAIL_ROW },
    ]);
  }
  if (regulatorBody.length > 1) pumpBlock.push(grid(["auto", "*", "auto"], regulatorBody, [0, 10, 0, 0]));
  const releaserRows: TableCell[][] = [[th("Releaser pump"), th("Make / model"), th("Motor")]];
  releaserPumpRows(config).forEach((p, i) => {
    if (isBlankPumpRow(p)) return;
    releaserRows.push([`Releaser ${i + 1}`, `${p.make ?? ""} ${p.model ?? ""}`.trim() || "—", motorText(p.motorSize)]);
  });
  if (releaserRows.length > 1) pumpBlock.push(grid(["auto", "*", "auto"], releaserRows, [0, 10, 0, 0]));

  // --- Numerical readings ----------------------------------------------------------------------
  // A table per ISO group with a reading entered, grouped by test family (the wizard's steps, in
  // flowchart order — allReadingSections' order), so each family prints as a part of its own.
  const readingBlocks = (sections: ReadingSection[]): Content[] => {
    const out: Content[] = [];
    for (const sec of sections) {
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
      out.push(subHeader(sec.title));
      out.push(grid(["*", 70, 80, 44], body));
    }
    return out;
  };
  const vacuumReadings = readingBlocks(testRecordSections(config, test.readings));
  const airflowReadings = readingBlocks(airflowSections(config, test.readings));
  const clusterReadings = readingBlocks(individualClusterSections(config));
  const pulsationReadings = readingBlocks(pulsatorSections(config, test.readings));
  const additionalReadings = readingBlocks(additionalTestSections(config, test.readings));

  // --- Per-unit rows ---------------------------------------------------------------------------
  // Each table prints with its family's readings: the pulsators under pulsation, the clusters
  // under the individual cluster tests.
  const pulsatorTable: Content[] = [];
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
    pulsatorTable.push(subHeader("Pulsator results"));
    pulsatorTable.push(grid(["*", "*", "*", "*", "*", "*", "*", "*"], body));
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
    if (bandParts.length) pulsatorTable.push({ text: bandParts, fontSize: 9, color: INK, margin: [0, 5, 0, 0] });
  }
  const clusterTable: Content[] = [];
  const clusterRows = recordedRows(test.clusterRows);
  if (clusterRows.length) {
    const body: TableCell[][] = [
      [th("Cluster no."), th("Total air admission"), th("Leakage"), th("Air-vent admission")],
      ...clusterRows.map((r) => [
        { text: r.unit, bold: true } as TableCell,
        ...["totalAirAdmission", "leakage", "airVent"].map((key) => r.values[key] ?? ""),
      ]),
    ];
    clusterTable.push(subHeader("Cluster results"));
    clusterTable.push(grid([70, "*", "*", "*"], body));
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

  const section = (part: keyof typeof PART_HEADINGS, blocks: Content[]): Content[] =>
    blocks.length > 0 ? [sectionHeader(PART_HEADINGS[part]), ...blocks] : [];

  return {
    // Page one: what the farmer acts on.
    summary: [
      letterhead,
      titleLine,
      detailsBlock,
      ...resultBanner,
      // A clean machine says so in the banner; an empty heading under it would read as missing.
      ...(faultBlock.length > 0 ? [sectionHeader("Fault summary & recommendations"), ...faultBlock] : []),
      notesBlock,
    ],
    // The working.
    machine: section("machine", [configBlock, ...pumpBlock]),
    // ISO 1–9 are the core of every test, so this part always prints — if only to say that
    // nothing was entered.
    vacuum: section("vacuum", vacuumReadings.length > 0 ? vacuumReadings : [{ text: "No readings entered.", fontSize: 9, color: MUTED }]),
    airflow: section("airflow", airflowReadings),
    cluster: section("cluster", [...clusterReadings, ...clusterTable]),
    pulsation: section("pulsation", [...pulsationReadings, ...pulsatorTable]),
    additional: section("additional", additionalReadings),
    // Migrated tests show their recorded faults in the Fault Summary; the recomputed visual-checks
    // section (driven by the empty visualFaults map) is omitted for them.
    visual: isLegacy ? [] : section("visual", visualBlock),
    analyser: attachmentBlock,
    audit: [...(attestRows.length > 0 ? [sectionHeader("Attestations"), ...attestRows] : []), ...amendmentBlock],
  };
}

/** The parts this test has something to print in, in report order, as the sign-off step's
 * section picker lists them. The summary, machine configuration and vacuum tests always print;
 * the rest only when there's something in them. */
export function reportPartOptions(test: LocalTest): ReportPartOption[] {
  const parts = reportParts(test);
  return REPORT_PARTS.filter((p) => parts[p].length > 0).map((part): ReportPartOption => {
    switch (part) {
      case "summary":
        return { part, label: "Test summary", hint: "Page one: farm and test details, faults, recommendations and comments" };
      case "analyser":
        return { part, label: "Pulsation analyser report", hint: `${test.pulsationPdf?.name ?? "The attached PDF"}, added at the end` };
      case "audit": {
        const amended = (test.amendments?.length ?? 0) > 0;
        const attested = test.attestations.length > 0;
        return { part, label: amended ? (attested ? "Attestations & amendment history" : "Amendment history") : "Attestations" };
      }
      default:
        return { part, label: PART_HEADINGS[part] };
    }
  });
}

/** The parts a print holds, in report order: those chosen that have something in them, or every
 * part that does when there was no choice. A choice with nothing printable in it gets the summary
 * rather than a blank document. */
function partsToPrint(parts: Record<ReportPart, Content[]>, only?: readonly ReportPart[]): ReportPart[] {
  const printed = REPORT_PARTS.filter((p) => parts[p].length > 0 && (!only || only.includes(p)));
  return printed.length > 0 ? printed : ["summary"];
}

/** The node, as the first thing in the document. A part that starts its own page (the amendment
 * history) would otherwise leave page one blank in front of it. */
function opensDocument(node: Content): Content {
  const { pageBreak, ...rest } = node as { pageBreak?: unknown };
  return (pageBreak ? rest : node) as Content;
}

/** Builds the pdfmake document definition for the Test Summary. Pure — unit-testable.
 * `calibrationFallback` is the tester's live profile calibration, used only for a test that
 * hasn't been stamped yet (a report previewed before sign-off); a completed test always
 * reprints its own stamped snapshot. `branding` is the testing company shown beside the MPNZ
 * mark; without it the letterhead carries MPNZ alone. `only` prints just those parts (the
 * sign-off step's section picker); without it, the full report. `generatedAt` pins the report to
 * that moment — the footer's "generated", the copyright year and the PDF's own dates — instead of
 * now, so the same test always lays out to the same bytes (the stored Final Report). */
export function buildTestSummaryDoc(
  test: LocalTest,
  calibrationFallback?: CalibrationDates,
  branding?: ReportBranding,
  testerFallback?: TesterDetails | null,
  only?: readonly ReportPart[],
  generatedAt?: string,
): TDocumentDefinitions {
  const parts = reportParts(test, calibrationFallback, branding, testerFallback);
  const printed = partsToPrint(parts, only);
  // With the summary, page one is the letterhead and the working starts on page two (see
  // WORKING_STARTS). Without it, every page is a working page — running header, plain footer —
  // and page one keeps the compliance disclaimer.
  const withSummary = printed[0] === "summary";
  const content: Content[] = [];
  printed.forEach((part, i) => {
    const [head, ...rest] = parts[part];
    content.push(i === 0 ? opensDocument(head) : withSummary && i === 1 ? startsWorking(head) : head, ...rest);
  });

  const farm = test.farm;
  const farmName = farm?.name ?? test.farmName ?? "—";
  const companyRaster = rasterLogo(branding?.companyLogo);
  const pinned = generatedAt ? new Date(generatedAt) : null;
  const at = pinned && !Number.isNaN(pinned.getTime()) ? pinned : null;
  const generated = (at ?? new Date()).toLocaleString("en-NZ", { timeZone: NZ_TIME_ZONE });
  const copyright = copyrightNotice(Number(nzDate((at ?? new Date()).toISOString()).slice(0, 4)));

  return {
    pageSize: "A4",
    pageMargins: [MARGIN_X, MARGIN_TOP, MARGIN_X, MARGIN_BOTTOM],
    // Pinned, the PDF's own dates are that moment too: they (and the file id PDFKit derives from
    // them) would otherwise make every generation's bytes different.
    info: { title: `Test Summary — ${farmName}`, ...(at ? { creationDate: at, modDate: at } : {}) },
    defaultStyle: { color: INK },
    ...(companyRaster ? { images: { [COMPANY_LOGO_IMAGE]: companyRaster } } : {}),
    // Page one carries the letterhead swirl and a flourish in the bottom corner, when it's the
    // summary, and the disclaimer whatever it is; later pages are left plain so the tables read
    // cleanly.
    background: (page, size) =>
      page !== 1
        ? null
        : withSummary
          ? [
              { svg: letterheadSwirlSvg(), width: PAGE_WIDTH, absolutePosition: { x: 0, y: SWIRL_TOP } },
              { svg: footerSwirlSvg(), width: 220, absolutePosition: { x: size.width - 220, y: size.height - 60 } },
              disclaimerAt(size.height),
            ]
          : [disclaimerAt(size.height)],
    // Working pages: the MPNZ mark, the report and farm name, the company logo when there is one,
    // and the lockup's tapered rule underneath. A fresh node per page: pdfmake lays out what it's
    // given.
    header: (page) =>
      page === 1 && withSummary
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
    // Every page: page number, copyright, the admin-managed privacy line and when it was generated.
    // The summary page keeps it all left of the corner flourish; working pages sit it under the
    // lockup's tapered rule, matching the running header.
    footer: (page, pages) => {
      const privacyFooter = getPrivacyContent().reportFooterText;
      const privacy: Content[] = privacyFooter ? [{ text: privacyFooter, fontSize: 6, color: MUTED, margin: [0, 1, 0, 0] } as Content] : [];
      if (page === 1 && withSummary) {
        return {
          margin: [MARGIN_X, DISCLAIMER_BAND + 14, MARGIN_X + 175, 0],
          stack: [
            { text: [{ text: `Page 1 of ${pages}`, color: INK }, `   ${copyright}`], fontSize: 6.5, color: MUTED },
            ...privacy,
            { text: `AutoRep · generated ${generated}`, fontSize: 6, color: MUTED, margin: [0, 1, 0, 0] },
          ],
        };
      }
      return {
        margin: [MARGIN_X, DISCLAIMER_BAND + 6, MARGIN_X, 0],
        stack: [
          { svg: ruleSwooshSvg(CONTENT_WIDTH), width: CONTENT_WIDTH, margin: [0, 0, 0, 4] },
          {
            columns: [
              { text: copyright, fontSize: 6.5, color: MUTED },
              { text: `Page ${page} of ${pages}`, alignment: "right", fontSize: 7, color: INK, width: 60 },
            ],
          },
          {
            columns: [
              { stack: privacy.length ? privacy : [{ text: "" }] },
              { text: `AutoRep · generated ${generated}`, alignment: "right", fontSize: 6, color: MUTED, width: 150, margin: [0, 1, 0, 0] },
            ],
          },
        ],
      };
    },
    // The working starts page two unless the summary already reaches it (WORKING_STARTS). And
    // keep a heading with what follows it: one that would land in the last stretch of a page
    // starts the next page instead (a "Visual checks" heading was stranded at a page foot).
    pageBreakBefore: (node) => {
      if (node.id === WORKING_STARTS && node.startPosition?.pageNumber === 1) return true;
      const at = node.startPosition?.verticalRatio ?? 0;
      return (node.headlineLevel === 1 && at > 0.86) || (node.headlineLevel === 2 && at > 0.92);
    },
    content,
  };
}

// The Amendment history page: one block per superseding version (ascending), each a table of
// Section / Field / Previous / Amended. Starts on its own page — it's the audit appendix.
function buildAmendmentBlock(test: LocalTest): Content[] {
  const amendments = [...(test.amendments ?? [])].sort((a, b) => a.version - b.version);
  if (amendments.length === 0) return [];

  const out: Content[] = [
    startsPage(sectionHeader("Amendment history")),
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

// Fault Summary block for a migrated test: recorded faults + section recommendations, exactly as
// recorded (no recompute). Its comment prints in the general comments box, as a live test's does.
function recordedFaultBlock(test: LocalTest): Content[] {
  const recs = test.recordedRecommendations ?? [];
  const faults = test.recordedVisualFaults ?? [];
  if (faults.length === 0 && recs.length === 0) {
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

/** The report's file name: the farm and the day of sign-off, marked when only some of the
 * sections were printed so a partial copy can't pass for the full report in someone's inbox. */
export function reportFileName(test: LocalTest, only?: readonly ReportPart[]): string {
  const farm = (test.farm?.name ?? test.farmName ?? "farm").replace(/[^\w\- ]+/g, "");
  const partial = only != null && reportPartOptions(test).some((o) => !only.includes(o.part));
  return `Test Summary - ${farm} - ${reportDateStamp(test.markedCompleteAt ?? test.updatedAt)}${partial ? " - selected sections" : ""}.pdf`;
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

/** How the attached pulsation analyser PDF fared in a generated report:
 *  - `none`: no attachment, or the analyser wasn't among the parts chosen;
 *  - `appended`: its pages are on the end;
 *  - `unreachable`: its bytes are on the server only, and the server couldn't be reached;
 *  - `unreadable`: pdf-lib couldn't read it (damaged or encrypted) — it never will;
 *  - `merger-unavailable`: pdf-lib itself couldn't be loaded (its lazy chunk isn't on the device).
 * Whenever it isn't `appended`, the report holds every other part and no analyser pages. */
export type AnalyserOutcome = "none" | "appended" | "unreachable" | "unreadable" | "merger-unavailable";

export interface ReportOptions {
  /** The company on the letterhead — the read-only server view gives the one the test was done
   * for; on the tester's own device it comes from what the device has cached. */
  branding?: ReportBranding;
  /** Who did the test, when the test carries no stamped details (the server view's answer). */
  testerFallback?: TesterDetails | null;
  /** Just these parts — the analyser's pages among them only when "analyser" is chosen. */
  only?: readonly ReportPart[];
  /** Pin the report to this moment instead of now (see buildTestSummaryDoc). */
  generatedAt?: string;
  /** A read-only view of a test held on the server: the analyser PDF comes through the view's
   * route and nothing is kept on this device (sync/pulsationAttachment.ts attachmentBase64). */
  serverView?: boolean;
}

interface Generated {
  fileName: string;
  created: CreatedPdf;
  /** The report with the analyser's pages appended, when they were. */
  merged: Uint8Array | null;
  analyser: AnalyserOutcome;
}

/** Lays the report out for `printed`, from what this device holds right now: the cached
 * letterhead and tester details are read first — before pdfmake's chunks load, which can take a
 * while — then the cached standards and privacy footer as the document is built. pdfmake and the
 * fonts load as lazy chunks on first use: ReportGeneratorUnavailableError when they aren't on the
 * device and can't be fetched. */
async function layOut(test: LocalTest, printed: LocalTest, opts: ReportOptions): Promise<CreatedPdf> {
  const { branding, testerFallback, only, generatedAt } = opts;
  // A report previewed before sign-off has no stamped calibration yet — fall back to the
  // tester's current profile so the preview matches what sign-off will record.
  const calibration = test.markedCompleteAt ? undefined : await getCachedCalibration().catch(() => undefined);
  const letterhead =
    branding ?? brandingForTest(test, await getCachedCompanyBranding().catch(() => null));
  // A test stamped at sign-off names its own tester. Otherwise the server view says who (or null
  // for nobody known); on this device an unstamped test (a draft, or one signed off before the
  // stamp existed) is the signed-in tester's own.
  const tester = test.testedBy
    ?? (testerFallback !== undefined ? testerFallback : await getCachedTesterDetails().catch(() => null));

  const { pdfMake, vfs } = await loadPdfMake();
  // pdfmake 0.3.x: register the Roboto virtual file system.
  (pdfMake as { addVirtualFileSystem(v: unknown): void }).addVirtualFileSystem(vfs);
  return (pdfMake as { createPdf(doc: TDocumentDefinitions): CreatedPdf }).createPdf(
    buildTestSummaryDoc(printed, calibration, letterhead, tester, only, generatedAt),
  );
}

/** The report's pages with the analyser PDF's appended. Pinned, pdf-lib mustn't stamp its own
 * producer and modification time over the report's — the bytes must come out the same each time. */
async function appendAnalyser(
  report: Uint8Array,
  attachment: string,
  pinned: boolean,
): Promise<{ merged: Uint8Array; analyser: "appended" } | { merged: null; analyser: "unreadable" | "merger-unavailable" }> {
  let PDFDocument: typeof import("pdf-lib").PDFDocument;
  try {
    ({ PDFDocument } = await loadPdfLib());
  } catch {
    return { merged: null, analyser: "merger-unavailable" };
  }
  try {
    const doc = await PDFDocument.load(report, { updateMetadata: !pinned });
    const attachDoc = await PDFDocument.load(base64ToBytes(attachment));
    for (const page of await doc.copyPages(attachDoc, attachDoc.getPageIndices())) doc.addPage(page);
    return { merged: await doc.save(), analyser: "appended" };
  } catch {
    // Unreadable/encrypted attachment — the summary alone rather than nothing.
    return { merged: null, analyser: "unreadable" };
  }
}

/** Lays the report out and appends the analyser PDF's pages when wanted and possible. */
async function generate(test: LocalTest, opts: ReportOptions): Promise<Generated> {
  const fileName = reportFileName(test, opts.only);
  // The analyser PDF's bytes may live on the server only (the device lets them go a week after a
  // test is synced — sync/pulsationAttachment.ts): fetch them back first, and if that can't be done
  // right now, make the report without the attachment rather than claim one that isn't appended.
  const wantsAttachment = !!test.pulsationPdf && (!opts.only || opts.only.includes("analyser"));
  const attachment = wantsAttachment ? await attachmentBase64(test, { serverView: opts.serverView }) : null;
  const printed = wantsAttachment && !attachment ? { ...test, pulsationPdf: null } : test;

  const created = await layOut(test, printed, opts);
  if (!wantsAttachment) return { fileName, created, merged: null, analyser: "none" };
  if (!attachment) return { fileName, created, merged: null, analyser: "unreachable" };
  return { fileName, created, ...(await appendAnalyser(await pdfBuffer(created), attachment, !!opts.generatedAt)) };
}

export interface ReportPdf {
  bytes: Uint8Array;
  fileName: string;
  analyser: AnalyserOutcome;
}

/** The report as bytes — what downloadTestSummaryPdf saves, for anything that needs to keep or
 * send it rather than hand it to the browser. */
export async function reportPdfBytes(test: LocalTest, opts: ReportOptions = {}): Promise<ReportPdf> {
  const g = await generate(test, opts);
  return { bytes: g.merged ?? (await pdfBuffer(g.created)), fileName: g.fileName, analyser: g.analyser };
}

/** The Final Report as signed off: every part, the analyser's pages on the end, pinned to the
 * moment of sign-off — so generating it again for the same test, from the same inputs, gives the
 * same bytes. Made from what the device holds when it's called: sync/finalReportUpload.ts sends this
 * only when nothing was captured at sign-off (captureFinalReport). */
export function finalReportPdf(test: LocalTest): Promise<ReportPdf> {
  return reportPdfBytes(test, { generatedAt: test.markedCompleteAt ?? undefined });
}

/**
 * The Final Report's own pages as they are at sign-off — every part, pinned to the sign-off time,
 * laid out from the standards, letterhead and privacy footer the device holds NOW — everything but
 * the analyser PDF's pages, which completeFinalReport appends when it's sent. Captured at sign-off,
 * so a standards update or a new company logo arriving before the upload can't change the stored
 * copy (it would have recomputed pass/fail). Small, because the analyser PDF — up to 15 MB, and
 * already kept on the device or the server — isn't in it. Needs no connection: never fetches the
 * analyser PDF, whose note in the report comes from the attachment's name and date alone.
 */
export async function captureFinalReport(test: LocalTest): Promise<Uint8Array> {
  return pdfBuffer(await layOut(test, test, { generatedAt: test.markedCompleteAt ?? undefined }));
}

/** The Final Report from its captured pages: the analyser PDF's pages appended exactly as
 * finalReportPdf appends them, so from the same inputs the two give the same bytes. `unreachable`
 * (nothing appended) when the analyser's bytes are on the server and it can't be reached. */
export async function completeFinalReport(test: LocalTest, captured: Uint8Array): Promise<ReportPdf> {
  const fileName = reportFileName(test);
  if (!test.pulsationPdf) return { bytes: captured, fileName, analyser: "none" };
  const attachment = await attachmentBase64(test);
  if (!attachment) return { bytes: captured, fileName, analyser: "unreachable" };
  const merge = await appendAnalyser(captured, attachment, true);
  return { bytes: merge.merged ?? captured, fileName, analyser: merge.analyser };
}

/** Generates and downloads the PDF; the attached pulsation analyser report (if any) is appended
 * page-for-page. See ReportOptions for `branding`, `testerFallback`, `only` and `serverView`. */
export async function downloadTestSummaryPdf(
  test: LocalTest,
  branding?: ReportBranding,
  testerFallback?: TesterDetails | null,
  only?: readonly ReportPart[],
  serverView?: boolean,
): Promise<void> {
  const g = await generate(test, { branding, testerFallback, only, serverView });
  if (g.analyser === "unreachable") {
    const { showToast } = await import("../ui/toast");
    showToast(
      "The pulsation analyser PDF is kept on the server, and this device can't reach it right now — " +
        "the report was made without it. Connect and download again to include it.",
      "error",
      9000,
    );
  } else if (g.analyser === "unreadable" || g.analyser === "merger-unavailable") {
    const { showToast } = await import("../ui/toast");
    showToast("The attached PDF could not be appended — downloaded the summary without it.", "error");
  }
  if (g.merged) {
    downloadBlob(g.merged, g.fileName);
    return;
  }
  g.created.download(g.fileName);
}

/** Saves bytes this device was handed (the stored report as signed off) the way a generated
 * report is saved. */
export function savePdf(bytes: Uint8Array, fileName: string): void {
  downloadBlob(bytes, fileName);
}
