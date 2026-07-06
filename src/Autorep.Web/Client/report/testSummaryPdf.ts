// Test Summary report (M4) — generated entirely on-device from the LocalTest so it works
// offline. pdfmake (+ Roboto fonts) loads as a lazy chunk only when a report is requested.
import type { Content, TDocumentDefinitions, TableCell } from "pdfmake/interfaces";
import type { LocalTest } from "../db/testStore";
import { aggregate } from "../faults/faultAggregator";
import { buildFaultInputs } from "../faults/buildFaults";
import { allReadingSections } from "../passfail/standards";
import { evaluate, type PassFailRule } from "../passfail/passFail";
import { preStartSections, runningSectionsFor } from "../wizard/visualChecklist";
import { resolveWizard } from "../wizard/wizardStepResolver";
import { pulsationLimits, pulsatorSummary } from "../passfail/pulsatorStats";
import { getPrivacyContent } from "../config/privacyContent";

const BRAND = "#003893";
const MUTED = "#64748b";
const PASS = "#16a34a";
const FAIL = "#dc2626";

function describeRule(rule: PassFailRule, unit: string): string {
  switch (rule.kind) {
    case "atMost": return `≤ ${rule.limit} ${unit}`;
    case "atLeast": return `≥ ${rule.min} ${unit}`;
    case "between": return `${rule.min}–${rule.max} ${unit}`;
    case "tolerance": return `± ${rule.tolerance} ${unit}`;
    default: return "—";
  }
}

function fmtDate(iso?: string | null): string {
  if (!iso) return "—";
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? String(iso) : d.toLocaleString("en-NZ");
}

const th = (text: string): TableCell => ({ text, bold: true, fontSize: 8, color: MUTED });

function sectionHeader(text: string): Content {
  return { text, fontSize: 12, bold: true, color: BRAND, margin: [0, 14, 0, 4] };
}

/** Builds the pdfmake document definition for the Test Summary. Pure — unit-testable. */
export function buildTestSummaryDoc(test: LocalTest): TDocumentDefinitions {
  const config = test.config;
  const summary = aggregate(buildFaultInputs(test));
  const completed = fmtDate(test.markedCompleteAt);
  // Migrated tests carry faults/recommendations + verdicts as recorded; reprint those faithfully
  // rather than recomputing against today's standards.
  const isLegacy = test.recordedRecommendations !== undefined;

  // --- Farm + configuration ----------------------------------------------------------------
  const farm = test.farm;
  const farmLines: Content = {
    columns: [
      {
        width: "*",
        stack: [
          { text: "Farm", fontSize: 8, color: MUTED },
          { text: farm?.name ?? test.farmName ?? "—", fontSize: 10 },
          { text: [farm?.addressLine1, farm?.addressLine2, farm?.town, farm?.postCode].filter(Boolean).join(", ") || "—", fontSize: 9, color: MUTED },
          { text: `Supply no: ${farm?.supplyNumber ?? "—"} · ${farm?.milkCompanyName ?? "—"} · ${farm?.regionName ?? "—"}`, fontSize: 9, color: MUTED },
        ],
      },
      {
        width: "*",
        stack: [
          { text: "Test", fontSize: 8, color: MUTED },
          { text: `Completed: ${completed}`, fontSize: 10 },
          { text: `Farmer: ${farm?.farmerName ?? "—"} · ${farm?.contactPhone ?? "—"}`, fontSize: 9, color: MUTED },
          {
            text: `Calibration expiry — airflow: ${test.calAirFlowMeters ?? "—"} · pulsator: ${test.calPulsatorTesters ?? "—"} · vacuum: ${test.calVacuumGauges ?? "—"}`,
            fontSize: 9, color: MUTED,
          },
        ],
      },
    ],
    columnGap: 16,
  };

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

  const configBlock: Content = {
    table: {
      widths: ["auto", "*", "auto", "*"],
      body: [
        [th("Plant"), `${config.plantType} · ${config.plantSize ?? "—"}`, th("Clusters"), String(config.clusterCount || "—")],
        [th("Pulsators"), `${config.pulsatorCount || "—"} · ${config.pulsatorBrand ?? "—"} ${config.pulsatorModel ?? ""}`.trim(), th("Configuration"), config.pulsatorConfiguration ?? "—"],
        [th("Shell"), config.shellModel ?? "—", th("Claw"), config.clawModel ?? "—"],
        [th("Liners (F/B)"), `${config.linerModel ?? "—"} / ${config.backLiner ?? "—"}`, th("Milkline"), config.milklineSize ? `${config.milklineSize} mm` : "—"],
        [th("Vacuum pumps"), `${config.numberOfVacuumPumps} · ${config.pumpLubrication}`, th("Atmos. pressure"), config.atmosPressureSeaLevel ? `${config.atmosPressureSeaLevel} kPa` : "—"],
        [th("Equipment"), { text: flags.join(", ") || "—", colSpan: 3 }, "", ""],
      ],
    },
    layout: "lightHorizontalLines",
    fontSize: 9,
  };

  // --- Fault summary -------------------------------------------------------------------------
  const faultRows: TableCell[][] = [[th("Severity"), th("Area"), th("Fault"), th("Recommendation")]];
  for (const g of summary.groups) {
    for (const f of g.faults) {
      faultRows.push([
        { text: f.severity, color: f.severity === "Minor" ? MUTED : FAIL, fontSize: 9 },
        { text: g.component, fontSize: 9 },
        { text: f.description, fontSize: 9 },
        { text: f.recommendation ?? "", fontSize: 9 },
      ]);
    }
  }
  const faultBlock: Content[] = isLegacy
    ? recordedFaultBlock(test)
    : summary.total === 0
      ? [{ text: "No faults recorded — the machine passed every completed check.", color: PASS, fontSize: 10 }]
      : [
          { text: `${summary.critical} critical · ${summary.major} major · ${summary.minor} minor`, fontSize: 10, margin: [0, 0, 0, 4] },
          { table: { widths: ["auto", "auto", "*", "*"], body: faultRows }, layout: "lightHorizontalLines" },
        ];

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
      body.push([
        { text: r.label, fontSize: 9 },
        { text: `${v} ${r.unit}`, fontSize: 9 },
        { text: describeRule(r.rule, r.unit), fontSize: 9, color: MUTED },
        verdict === "noStandard"
          ? { text: "—", color: MUTED, fontSize: 9 }
          : { text: verdict.toUpperCase(), color: verdict === "pass" ? PASS : FAIL, bold: true, fontSize: 9 },
      ]);
    }
    readingBlocks.push({ text: sec.title, fontSize: 10, bold: true, margin: [0, 8, 0, 2] });
    readingBlocks.push({ table: { widths: ["*", "auto", "auto", "auto"], body }, layout: "lightHorizontalLines" });
  }

  // --- Per-unit rows ---------------------------------------------------------------------------
  const unitBlocks: Content[] = [];
  if (test.pulsatorRows?.length) {
    const s = pulsatorSummary(test.pulsatorRows);
    const limits = pulsationLimits();
    const body: TableCell[][] = [
      [th("Pulsator"), th("Rate (ppm)"), th("Ratio F (%)"), th("Ratio B (%)"), th("Phase b (%)"), th("Phase d (ms)"), th("Max vac (kPa)"), th("Limp (%)")],
      ...test.pulsatorRows.map((r) => [
        { text: r.unit, fontSize: 9 },
        ...["rate", "ratioFront", "ratioBack", "phaseB", "phaseDms", "maxVacuum", "limp"].map((k) => ({
          text: r.values[k] ?? "", fontSize: 9,
        } as TableCell)),
      ]),
    ];
    unitBlocks.push(sectionHeader("Pulsator test results"));
    unitBlocks.push({ table: { widths: ["auto", "auto", "auto", "auto", "auto", "auto", "auto", "auto"], body }, layout: "lightHorizontalLines" });
    const spreadText: Content = {
      text: [
        `Rate ${s.slowestRate ?? "—"}–${s.fastestRate ?? "—"} ppm (spread ${s.rateSpread ?? "—"}, limit ${limits.rateSpreadMax}) `,
        { text: s.rateSpreadOk == null ? "" : s.rateSpreadOk ? " PASS" : " FAIL", color: s.rateSpreadOk ? PASS : FAIL, bold: true },
        `   ·   Ratio spread ${s.ratioSpread ?? "—"} (limit ${limits.ratioSpreadMax})`,
        { text: s.ratioSpreadOk == null ? "" : s.ratioSpreadOk ? " PASS" : " FAIL", color: s.ratioSpreadOk ? PASS : FAIL, bold: true },
      ],
      fontSize: 9,
      margin: [0, 4, 0, 0],
    };
    unitBlocks.push(spreadText);
  }
  if (test.clusterRows?.length) {
    const body: TableCell[][] = [
      [th("Cluster"), th("Total air admission"), th("Leakage"), th("Air-vent admission")],
      ...test.clusterRows.map((r) => [
        { text: r.unit, fontSize: 9 },
        ...["totalAirAdmission", "leakage", "airVent"].map((k) => ({ text: r.values[k] ?? "", fontSize: 9 } as TableCell)),
      ]),
    ];
    unitBlocks.push(sectionHeader("Individual cluster tests"));
    unitBlocks.push({ table: { widths: ["auto", "*", "*", "*"], body }, layout: "lightHorizontalLines" });
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
          { text: sec.title, fontSize: 9 },
          { text: it.label, fontSize: 9 },
          { text: e.observation ?? e.note ?? "—", fontSize: 9 },
          { text: e.severity ?? "Major", fontSize: 9, color: e.severity === "Minor" ? MUTED : FAIL },
        ]);
      }
    }
  }
  const visualBlock: Content[] = [
    { text: `${okCount} item(s) verified OK · ${visualFaultRows.length - 1} fault(s) logged`, fontSize: 10, margin: [0, 0, 0, 4] },
  ];
  if (visualFaultRows.length > 1) {
    visualBlock.push({ table: { widths: ["auto", "*", "*", "auto"], body: visualFaultRows }, layout: "lightHorizontalLines" });
  }

  // --- Attestations ---------------------------------------------------------------------------
  const attestRows = test.attestations.map((a) => ({
    text: `${fmtDate(a.attestedAt)} — ${a.step}${a.section ? ` · ${a.section}` : ""}: "${a.text}"`,
    fontSize: 8,
    color: MUTED,
    margin: [0, 1, 0, 0] as [number, number, number, number],
  }));

  // --- Amendment history -----------------------------------------------------------------------
  // A re-edited test carries its cumulative amendment chain; render it as the report's final
  // page so every change made after the original sign-off is visible on the printed record.
  const amendmentBlock: Content[] = buildAmendmentBlock(test);

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

  return {
    pageSize: "A4",
    pageMargins: [40, 48, 40, 48],
    info: { title: `Test Summary — ${farm?.name ?? test.farmName}` },
    footer: (page, pages) => {
      const privacyFooter = getPrivacyContent().reportFooterText;
      return {
        stack: [
          {
            columns: [
              { text: `AutoRep · generated ${new Date().toLocaleString("en-NZ")}`, fontSize: 7, color: MUTED, margin: [40, 0, 0, 0] },
              { text: `${page} / ${pages}`, alignment: "right", fontSize: 7, color: MUTED, margin: [0, 0, 40, 0] },
            ],
          },
          ...(privacyFooter
            ? [{ text: privacyFooter, fontSize: 6, color: MUTED, margin: [40, 2, 40, 0] } as Content]
            : []),
        ],
      };
    },
    content: [
      { text: "Milking Machine Test Summary", fontSize: 16, bold: true, color: BRAND },
      { text: "NZMPTA AutoRep", fontSize: 9, color: MUTED, margin: [0, 0, 0, (test.version ?? 1) > 1 ? 2 : 10] },
      ...((test.version ?? 1) > 1
        ? [{
            text: `Version ${test.version} — supersedes an earlier completed test${
              amendmentBlock.length > 0 ? " (all changes are listed in the Amendment history section)" : ""
            }`,
            fontSize: 9, bold: true, color: BRAND, margin: [0, 0, 0, 10],
          } as Content]
        : []),
      farmLines,
      sectionHeader("Machine configuration"),
      configBlock,
      sectionHeader("Fault summary & recommendations"),
      ...faultBlock,
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
      fontSize: 10, bold: true, margin: [0, 8, 0, 2],
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
    out.push({ table: { widths: ["auto", "*", "*", "*"], body }, layout: "lightHorizontalLines" });
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
    return [{ text: "No faults or recommendations were recorded for this test.", color: PASS, fontSize: 10 }];
  }
  const out: Content[] = [];
  if (faults.length > 0) {
    out.push({ text: "Recorded faults", fontSize: 10, bold: true, margin: [0, 4, 0, 2] });
    out.push({ ul: faults, fontSize: 9 });
  }
  for (const r of recs) {
    out.push({ text: r.label, fontSize: 10, bold: true, margin: [0, 6, 0, 2] });
    out.push({ text: r.text, fontSize: 9 });
  }
  if (comment) {
    out.push({ text: "Tester comment", fontSize: 10, bold: true, margin: [0, 6, 0, 2] });
    out.push({ text: comment, fontSize: 9 });
  }
  return out;
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

/** Generates and downloads the PDF; the attached pulsation analyser report (if any) is appended
 * page-for-page. pdfmake, the fonts and pdf-lib all load as lazy chunks on first use. */
export async function downloadTestSummaryPdf(test: LocalTest): Promise<void> {
  const [pdfMakeModule, vfsModule] = await Promise.all([
    import("pdfmake/build/pdfmake"),
    import("pdfmake/build/vfs_fonts"),
  ]);
  const pdfMake = (pdfMakeModule as { default?: unknown }).default ?? pdfMakeModule;
  const vfs = (vfsModule as { default?: unknown }).default ?? vfsModule;
  // pdfmake 0.3.x: register the Roboto virtual file system.
  (pdfMake as { addVirtualFileSystem(v: unknown): void }).addVirtualFileSystem(vfs);

  const name = `Test Summary - ${(test.farm?.name ?? test.farmName ?? "farm").replace(/[^\w\- ]+/g, "")} - ${
    (test.markedCompleteAt ?? test.updatedAt).slice(0, 10)
  }.pdf`;
  const created = (pdfMake as { createPdf(doc: TDocumentDefinitions): CreatedPdf }).createPdf(
    buildTestSummaryDoc(test),
  );

  if (test.pulsationPdf) {
    try {
      const { PDFDocument } = await import("pdf-lib");
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
