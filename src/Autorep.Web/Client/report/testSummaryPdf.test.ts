import { describe, it, expect } from "vitest";
import type { TDocumentDefinitions } from "pdfmake/interfaces";
import {
  ANNUAL_TEST_NOTE,
  COMPLIANCE_DISCLAIMER,
  NO_GENERAL_COMMENTS,
  REPORT_PARTS,
  SEVERITY_MEANING,
  brandingForTest,
  copyrightNotice,
  buildTestSummaryDoc,
  reportDateStamp,
  reportFileName,
  reportPartOptions,
  type ReportPart,
} from "./testSummaryPdf";
import { defaultMachineConfiguration } from "../wizard/types";
import { allReadingSections } from "../passfail/standards";
import type { LocalTest } from "../db/testStore";

function sampleTest(): LocalTest {
  const now = "2026-06-11T00:00:00.000Z";
  return {
    id: "t1",
    farmName: "Sunny Acres",
    farm: { name: "Sunny Acres", supplyNumber: "12345", milkCompanyName: "Fonterra" },
    config: { ...defaultMachineConfiguration(), clusterCount: 20, pulsatorCount: 10 },
    currentStep: "ReviewSignOff",
    visualFaults: {
      "vp.wick": { status: "fault", severity: "Minor", observation: "Oil Wicks Dirty" },
      "vp.oilWater": { status: "ok" },
    },
    attestations: [{ step: "ReviewSignOff", attestedAt: now, text: "I confirm." }],
    readings: { "tr.workingVacuum": 48, "tr.airlineDropRR": 2 },
    recommendations: {},
    dataFields: {},
    pulsatorRows: [{ id: "1", unit: "1", values: { rate: "60", ratioFront: "62" } }],
    createdAt: now,
    updatedAt: now,
    markedCompleteAt: now,
    syncState: "uploaded",
  };
}

describe("buildTestSummaryDoc — calibration", () => {
  it("reprints the stamped snapshot on a completed test, ignoring the live profile", () => {
    const t = sampleTest();
    t.calAirFlowMeters = "2027-01-27";
    const json = JSON.stringify(
      buildTestSummaryDoc(t, { airFlowMeters: "2030-01-01" }).content,
    );
    expect(json).toMatch(/Airflow meter[^\]]*27\/01\/2027/);
    expect(json).not.toContain("01/01/2030");
  });

  it("falls back to the tester's profile when the test has no stamp yet (pre-sign-off preview)", () => {
    const t = sampleTest();
    const json = JSON.stringify(
      buildTestSummaryDoc(t, { airFlowMeters: "2027-03-04", vacuumGauges: "2027-05-06" }).content,
    );
    expect(json).toMatch(/Airflow meter[^\]]*04\/03\/2027/);
    expect(json).toMatch(/Vacuum gauge[^\]]*06\/05\/2027/);
    expect(json).toMatch(/Pulsation tester[^\]]*"—"/);
  });

  it("shows an em dash when neither a stamp nor a profile date exists", () => {
    const json = JSON.stringify(buildTestSummaryDoc(sampleTest()).content);
    for (const label of ["Airflow meter", "Pulsation tester", "Vacuum gauge"]) {
      expect(json).toMatch(new RegExp(`${label}[^\\]]*"—"`));
    }
  });
});

describe("reportDateStamp", () => {
  it("names the file for the local date of sign-off, not the UTC date", () => {
    // A test completed at 8:41 am NZST on 15 Sep 2026 is 20:41Z on the 14th; the old slice(0, 10)
    // named it "… 2026-09-14.pdf".
    expect(reportDateStamp("2026-09-14T20:41:48.000Z", "Pacific/Auckland")).toBe("2026-09-15");
    expect(reportDateStamp("2026-09-14T20:41:48.000Z", "UTC")).toBe("2026-09-14");
  });

  it("defaults to New Zealand time whatever the device is set to", () => {
    // The default parameter, not the device zone: a laptop left on UTC still names the file for
    // the NZ day, and the report's Completed line agrees with it.
    expect(reportDateStamp("2026-09-14T20:41:48.000Z")).toBe("2026-09-15");
    const t = sampleTest();
    t.markedCompleteAt = "2026-09-14T20:41:48.000Z";
    const json = JSON.stringify(buildTestSummaryDoc(t).content);
    expect(json).toContain("Tested 15 September 2026");
    expect(json).toContain("15/09/2026");
  });

  it("falls back to the raw date for an unparseable timestamp", () => {
    expect(reportDateStamp("2026-09-15", "UTC")).toBe("2026-09-15");
    expect(reportDateStamp("not-a-date")).toBe("not-a-date");
  });
});

describe("buildTestSummaryDoc", () => {
  it("builds a document with the core sections and farm name", () => {
    const doc = buildTestSummaryDoc(sampleTest());
    const json = JSON.stringify(doc.content);
    expect(json).toContain("Milking Machine Test Summary");
    expect(json).toContain("Sunny Acres");
    expect(json).toContain("Fault summary");
    expect(json).toContain("Vacuum tests (ISO 1–9)");
    expect(json).toContain("Pulsator results");
  });

  it("marks failed readings FAIL and includes the visual fault with its observation", () => {
    const doc = buildTestSummaryDoc(sampleTest());
    const json = JSON.stringify(doc.content);
    // airlineDropRR = 2 against the ≤ 1 standard fails.
    expect(json).toContain("FAIL");
    expect(json).toContain("Oil Wicks Dirty");
    // workingVacuum 48 ≤ 50 passes.
    expect(json).toContain("PASS");
  });

  it("reports a clean machine when nothing failed", () => {
    const t = sampleTest();
    t.visualFaults = { "vp.oilWater": { status: "ok" } };
    t.readings = { "tr.workingVacuum": 48 };
    const doc = buildTestSummaryDoc(t);
    expect(JSON.stringify(doc.content)).toContain("No faults recorded");
  });

  it("reprints a migrated test as-recorded (stored verdicts + recorded faults/recs, no recomputed visual section)", () => {
    const base = sampleTest();
    const t: LocalTest = {
      ...base,
      visualFaults: {},
      pulsatorRows: undefined,
      readings: { "tr.workingVacuum": 48 }, // 48 ≤ 50 would PASS on recompute…
      verdicts: { "tr.workingVacuum": "fail" }, // …but it was recorded as FAIL
      recordedRecommendations: [{ label: "Visual faults", text: "Replaced V-belts and cleaned filters." }],
      recordedVisualFaults: ["Vee Belts Require Replacement"],
      readonly: true,
    };
    const json = JSON.stringify(buildTestSummaryDoc(t).content);
    expect(json).toContain("Replaced V-belts and cleaned filters."); // recorded recommendation
    expect(json).toContain("Vee Belts Require Replacement"); // recorded fault
    expect(json).toContain("Recorded faults");
    expect(json).not.toContain("Visual checks"); // recomputed visual section omitted for migrated
    expect(json).toContain("FAIL"); // the as-recorded verdict is honoured
    expect(json).not.toContain("PASS"); // a recompute would have said PASS — proves as-recorded
  });

  it("renders the Amendment history as a final page when the test carries amendments", () => {
    const t = sampleTest();
    t.version = 2;
    t.supersedesId = "t0";
    t.amendments = [
      {
        version: 2,
        amendedAt: "2026-07-06T00:00:00.000Z",
        amendedBy: "tester@local",
        baseVersion: 1,
        baseCompletedAt: "2026-06-11T00:00:00.000Z",
        changes: [
          { section: "Numerical readings", label: "Working vacuum", from: "48 kPa", to: "50 kPa" },
        ],
      },
    ];
    const json = JSON.stringify(buildTestSummaryDoc(t).content);
    expect(json).toContain("Amendment history");
    expect(json).toContain('"pageBreak":"before"'); // it starts on its own page
    expect(json).toContain("supersedes version 1");
    expect(json).toContain("by tester@local"); // the WHO of the audit trail
    expect(json).toContain("48 kPa");
    expect(json).toContain("50 kPa");
  });

  // O2 / PRD 50 and 67: an administrator's version names them, their role and the reason they gave;
  // a merged version says what it combined and lists the fields both versions changed.
  it("names an administrator's role and reason, and lists what a merge combined", () => {
    const t = sampleTest();
    t.version = 3;
    t.amendments = [
      {
        version: 2,
        amendedAt: "2026-10-06T00:00:00.000Z",
        amendedBy: "sam@nzmpta",
        amendedByName: "Sam Superadmin",
        amendedByRole: "Super Administrator",
        reason: "Wording corrected for the farmer",
        baseVersion: 1,
        changes: [{ section: "Other", label: "General comments", from: "a", to: "b" }],
      },
      {
        version: 3,
        amendedAt: "2026-10-07T00:00:00.000Z",
        amendedBy: "tester@local",
        amendedByRole: "Automatic merge",
        baseVersion: 2,
        changes: [],
        merge: {
          headVersion: 2,
          headBy: "Sam Superadmin",
          fromVersion: 2,
          fromId: "tester-v2",
          fromBy: "tester@local",
          overlaps: [
            { section: "Other", label: "General comments", previous: "a", head: "b", incoming: "c", kept: "incoming" },
          ],
        },
      },
    ];

    const json = JSON.stringify(buildTestSummaryDoc(t).content);

    expect(json).toContain("by Sam Superadmin (Super Administrator)");
    expect(json).toContain("Reason: Wording corrected for the farmer");
    expect(json).toContain("Combined automatically: version 2 (Sam Superadmin) and version 2 (tester@local)");
    expect(json).toContain("Field (changed in both)");
    expect(json).toContain("c (kept)");
    expect(json).not.toContain("(Automatic merge)");
  });

  it("notes a re-completion with no data changes, and omits the section entirely for v1 tests", () => {
    const t = sampleTest();
    t.version = 2;
    t.amendments = [
      { version: 2, amendedAt: "2026-07-06T00:00:00.000Z", baseVersion: 1, changes: [] },
    ];
    expect(JSON.stringify(buildTestSummaryDoc(t).content)).toContain("Re-completed with no data changes");

    const v1 = sampleTest();
    expect(JSON.stringify(buildTestSummaryDoc(v1).content)).not.toContain("Amendment history");
  });

  it("judges the pulsator spread on the analyser's extremes in the results table, never on the recorded units", () => {
    // Two recorded units 10 ppm apart: the table lists them, but no spread verdict comes from them.
    const t = sampleTest();
    t.pulsatorRows = [
      { id: "1", unit: "3", values: { rate: "60" } },
      { id: "2", unit: "9", values: { rate: "70" } },
    ];
    const rowsOnly = JSON.stringify(buildTestSummaryDoc(t).content);
    expect(rowsOnly).toContain("Pulsator results");
    expect(rowsOnly).not.toContain("spread");
    // With the machine-level fastest/slowest entered, the calculated spread carries the verdict.
    t.readings = { ...t.readings, "puls.rateFastest": 62, "puls.rateSlowest": 59.5, "puls.rateSpread": 2.5 };
    const json = JSON.stringify(buildTestSummaryDoc(t).content);
    expect(json).toContain("Rate spread");
    expect(json).toContain("2.5 ppm");
  });

  it("prints recorded measurements and choices under the visual checks", () => {
    const t = sampleTest();
    t.dataFields = { "lpt.tubeType": "Twin", "lpt.lengthValue": "1500", "ba.clusterPositionType": "Side" };
    const json = JSON.stringify(buildTestSummaryDoc(t).content);
    expect(json).toContain("Recorded measurements");
    expect(json).toContain("Tube type");
    expect(json).toContain("Twin");
    expect(json).toContain("1500");
    expect(json).toContain("Side");
    // Nothing recorded → no table.
    expect(JSON.stringify(buildTestSummaryDoc(sampleTest()).content)).not.toContain("Recorded measurements");
  });

  it("leaves an added-but-never-filled row off the report", () => {
    const t = sampleTest();
    t.pulsatorRows = [{ id: "blank", unit: "", values: {} }];
    t.clusterRows = [{ id: "blank2", unit: "", values: { totalAirAdmission: "" } }];
    const json = JSON.stringify(buildTestSummaryDoc(t).content);
    expect(json).not.toContain("Pulsator results");
    expect(json).not.toContain("Individual cluster tests");
  });

  it("prints the pump details, so a replacement can be quoted off the report", () => {
    const t = sampleTest();
    t.config = {
      ...t.config,
      numberOfVacuumPumps: 2,
      vacuumPumps: [
        { make: "MASPORT", model: "RVP4000", motorSize: "7.5", drivesMilkPump: true, regulatorType: "Servo" },
        {},
      ],
      hasReleaserPump: true,
      releaserPumps: [{ make: "READ", model: "WR1200", motorSize: "2.2 hp" }],
    };
    const json = JSON.stringify(buildTestSummaryDoc(t).content);
    expect(json).toContain("MASPORT RVP4000");
    expect(json).toContain("7.5 kW"); // a bare number reads as kW
    expect(json).toContain("Servo");
    expect(json).toContain("READ WR1200");
    expect(json).toContain("2.2 hp"); // typed with its own unit, printed as typed
    expect(json).not.toContain("Pump 2"); // the row that was never filled in is left off
  });

  it("prints the regulators by type and count, and the suitability call only once answered", () => {
    const t = sampleTest();
    t.config = { ...t.config, regulators: [{ type: "Servo", quantity: 2 }, {}] };
    let json = JSON.stringify(buildTestSummaryDoc(t).content);
    expect(json).toContain("Servo");
    expect(json).toContain("\"2\"");
    expect(json).not.toContain("Regulator 2"); // the blank line is left off
    expect(json).not.toContain("Correct and big enough");

    t.config = { ...t.config, regulatorsSuitable: false };
    json = JSON.stringify(buildTestSummaryDoc(t).content);
    expect(json).toContain("Correct and big enough for this plant");
    expect(json).toMatch(/"text":"No","color":"#dc2626"/);
  });

  it("prints an older test's pump row holding only a regulator type as a regulator, not an empty pump row", () => {
    const t = sampleTest();
    t.config = { ...t.config, vacuumPumps: [{ regulatorType: "Servo" }] };
    delete (t.config as { regulators?: unknown }).regulators;
    const json = JSON.stringify(buildTestSummaryDoc(t).content);
    expect(json).not.toContain("Drives milk pump"); // no vacuum-pump table at all
    expect(json).toContain("Servo");
  });

  it("prints no pump tables when no pump details were captured", () => {
    expect(JSON.stringify(buildTestSummaryDoc(sampleTest()).content)).not.toContain("Make / model");
  });

  it("prints the configuration with display names, not enum names", () => {
    const json = JSON.stringify(buildTestSummaryDoc(sampleTest()).content);
    expect(json).toContain("Herringbone (lowline)");
    expect(json).toContain("Oil lubricated");
    expect(json).not.toContain("HerringboneLowline");
    expect(json).not.toContain("OilLubricated");
  });

  it("prints general comments under the fault summary", () => {
    const t = sampleTest();
    t.notes = "Regulation undershoot was excessive — VSD settings adjusted at time of test.";
    const json = JSON.stringify(buildTestSummaryDoc(t).content);
    expect(json).toContain("General comments");
    expect(json).toContain("VSD settings adjusted");
    expect(json).not.toContain(NO_GENERAL_COMMENTS);
    // The comments follow the fault table, ahead of the numbers.
    expect(json.indexOf("General comments")).toBeGreaterThan(json.indexOf("Fault summary"));
    expect(json.indexOf("General comments")).toBeLessThan(json.indexOf("Vacuum tests"));
  });

  it("says so when there are no general comments, rather than leaving the box off", () => {
    // None at all, or only whitespace.
    for (const notes of [undefined, "  \n "]) {
      const json = JSON.stringify(buildTestSummaryDoc({ ...sampleTest(), notes }).content);
      expect(json).toContain("General comments");
      expect(json).toContain(NO_GENERAL_COMMENTS);
      expect(json.indexOf(NO_GENERAL_COMMENTS)).toBeGreaterThan(json.indexOf("Fault summary"));
    }
    // A clean machine too: under its banner.
    const clean = { ...sampleTest(), visualFaults: {}, readings: {} };
    const json = JSON.stringify(buildTestSummaryDoc(clean).content);
    expect(json.indexOf(NO_GENERAL_COMMENTS)).toBeGreaterThan(json.indexOf("No faults found"));
  });

  it("prints a migrated test's comment in the same box, and says so when it has none", () => {
    const migrated: LocalTest = { ...sampleTest(), recordedRecommendations: [], recordedVisualFaults: [], readonly: true };
    const none = JSON.stringify(buildTestSummaryDoc(migrated).content);
    expect(none).toContain("No faults or recommendations were recorded for this test.");
    expect(none).toContain(NO_GENERAL_COMMENTS);

    const commented = JSON.stringify(buildTestSummaryDoc({ ...migrated, notes: "Replaced the pump belts on the day." }).content);
    expect(commented).toContain("Replaced the pump belts on the day.");
    expect(commented).not.toContain(NO_GENERAL_COMMENTS);
    expect(commented.match(/General comments/g)).toHaveLength(1); // the box, not a second heading
  });

  it("notes the appended pulsation PDF when one is attached", () => {
    const t = sampleTest();
    t.pulsationPdf = { name: "analyser-export.pdf", base64: "JVBERi0=", size: 1234, attachedAt: t.updatedAt };
    const json = JSON.stringify(buildTestSummaryDoc(t).content);
    expect(json).toContain("Attachments");
    expect(json).toContain("analyser-export.pdf");
    expect(json).toContain("appended to this document");
  });
});

describe("brandingForTest", () => {
  const current = { id: "c1", name: "Current Co", logo: "data:image/png;base64,AQID" };

  it("uses the cached company for a test stamped with it", () => {
    const t = { ...sampleTest(), testingCompanyId: "c1", testingCompanyName: "Current Co" };
    expect(brandingForTest(t, current)).toEqual({ companyName: "Current Co", companyLogo: current.logo });
  });

  it("prints only the stamped name when the tester has since changed company", () => {
    const t = { ...sampleTest(), testingCompanyId: "old", testingCompanyName: "Old Employer Ltd" };
    expect(brandingForTest(t, current)).toEqual({ companyName: "Old Employer Ltd" });
  });

  it("uses the tester's current company for an unstamped test (a draft, or signed off before the stamp)", () => {
    expect(brandingForTest(sampleTest(), current)).toEqual({ companyName: "Current Co", companyLogo: current.logo });
  });

  it("has no branding when there is no company either way", () => {
    expect(brandingForTest(sampleTest(), null)).toBeUndefined();
  });
});

describe("buildTestSummaryDoc — layout", () => {
  /** The index of the first top-level content node whose JSON contains the text. */
  const nodeIndex = (doc: ReturnType<typeof buildTestSummaryDoc>, text: string) =>
    (doc.content as unknown[]).findIndex((n) => JSON.stringify(n).includes(text));

  it("puts the farm, faults and comments on page one and starts the working on page two", () => {
    const t = sampleTest();
    t.notes = "Settings adjusted at time of test.";
    const doc = buildTestSummaryDoc(t);
    const content = doc.content as { id?: string; pageBreak?: string }[];
    const config = nodeIndex(doc, "Machine configuration");
    // pdfmake is asked to break before the machine configuration while it would sit on page one…
    const breaksBefore = (pageNumber: number) =>
      doc.pageBreakBefore!({ id: content[config].id, startPosition: { pageNumber, verticalRatio: 0.2 } } as never, {} as never);
    expect(breaksBefore(1)).toBe(true);
    // …but not once the summary has run onto page two: then it carries on under it.
    expect(breaksBefore(2)).toBe(false);
    // Nothing ahead of the machine configuration breaks the page.
    expect(content.slice(0, config).some((n) => n.pageBreak)).toBe(false);
    for (const pageOne of ["Sunny Acres", "Fault summary", "Oil Wicks Dirty", "General comments"]) {
      expect(nodeIndex(doc, pageOne)).toBeLessThan(config);
    }
    for (const later of ["Vacuum tests", "Pulsator results", "Visual checks"]) {
      expect(nodeIndex(doc, later)).toBeGreaterThan(config);
    }
  });

  it("leads page one with the MPNZ letterhead and leaves the running header to pages 2+", () => {
    const doc = buildTestSummaryDoc(sampleTest());
    expect(JSON.stringify((doc.content as unknown[])[0])).toContain("<svg");
    const header = doc.header as (page: number, pages: number, size: unknown) => unknown;
    expect(header(1, 3, {})).toBeNull();
    const running = JSON.stringify(header(2, 3, {}));
    expect(running).toContain("<svg");
    expect(running).toContain("Sunny Acres");
    expect(running).toContain("Supply 12345");
  });

  it("puts the testing company's logo and name on the letterhead when there is one", () => {
    const png = "data:image/png;base64,iVBORw0KGgo=";
    const doc = buildTestSummaryDoc(sampleTest(), undefined, { companyName: "Sample Testing Co. Ltd", companyLogo: png });
    const letterhead = JSON.stringify((doc.content as unknown[])[0]);
    expect(letterhead).toContain(`"image":"companyLogo"`);
    expect(doc.images).toEqual({ companyLogo: png }); // registered once, drawn by name
    expect(JSON.stringify(doc.content)).toContain("TESTED BY");
    expect(JSON.stringify(doc.content)).toContain("Sample Testing Co. Ltd");

    // An SVG upload is drawn as vectors.
    const svg = `data:image/svg+xml;base64,${btoa('<svg xmlns="http://www.w3.org/2000/svg"/>')}`;
    const svgHead = JSON.stringify((buildTestSummaryDoc(sampleTest(), undefined, { companyLogo: svg }).content as unknown[])[0]);
    expect(svgHead.match(/<svg/g)).toHaveLength(2); // MPNZ + the company's

    // A format pdfmake can't draw is left off rather than breaking the report.
    const webp = JSON.stringify((buildTestSummaryDoc(sampleTest(), undefined, { companyLogo: "data:image/webp;base64,UklGRg==" }).content as unknown[])[0]);
    expect(webp).not.toContain("image/webp");

    // No branding: MPNZ alone, no "Tested by".
    const plain = buildTestSummaryDoc(sampleTest());
    expect(JSON.stringify((plain.content as unknown[])[0]).match(/<svg/g)).toHaveLength(1);
    expect(JSON.stringify(plain.content)).not.toContain("TESTED BY");
  });

  it("judges a logo on its bytes, so mislabelled older data can't fail the report", () => {
    const head = (companyLogo: string) =>
      buildTestSummaryDoc(sampleTest(), undefined, { companyLogo });
    // A GIF labelled as PNG (pdfmake would throw on it): left off.
    const gif = head("data:image/png;base64,R0lGODlhAQABAAAAACw=");
    expect(gif.images).toBeUndefined();
    expect(JSON.stringify((gif.content as unknown[])[0])).not.toContain('"image"');
    // A JPEG labelled as PNG: drawn (pdfkit reads the real format from the bytes).
    expect(head("data:image/png;base64,/9j/4AAQSkZJRg==").images).toEqual({ companyLogo: "data:image/png;base64,/9j/4AAQSkZJRg==" });
    // Malformed SVG base64: left off rather than throwing.
    expect(() => head("data:image/svg+xml;base64,@@@")).not.toThrow();
  });

  it("decodes an SVG logo as UTF-8, keeping macrons in its text", () => {
    const svg = '<svg xmlns="http://www.w3.org/2000/svg"><text>Tāmaki Testing</text></svg>';
    const b64 = btoa(String.fromCharCode(...new TextEncoder().encode(svg)));
    const doc = buildTestSummaryDoc(sampleTest(), undefined, { companyLogo: `data:image/svg+xml;base64,${b64}` });
    expect(JSON.stringify((doc.content as unknown[])[0])).toContain("Tāmaki Testing");
  });

  it("repeats the company logo in the running header on pages 2+", () => {
    const png = "data:image/png;base64,iVBORw0KGgo=";
    const header = buildTestSummaryDoc(sampleTest(), undefined, { companyLogo: png }).header as (p: number, n: number, s: unknown) => unknown;
    expect(JSON.stringify(header(2, 3, {}))).toContain(`"image":"companyLogo"`);
    expect(JSON.stringify(header(3, 3, {}))).toContain(`"image":"companyLogo"`);
    const plain = buildTestSummaryDoc(sampleTest()).header as (p: number, n: number, s: unknown) => unknown;
    expect(JSON.stringify(plain(2, 3, {}))).not.toContain('"image"');
  });

  it("drops the empty fault heading when the machine is clean", () => {
    const t = sampleTest();
    t.visualFaults = { "vp.oilWater": { status: "ok" } };
    t.readings = { "tr.workingVacuum": 48 };
    const json = JSON.stringify(buildTestSummaryDoc(t).content);
    expect(json).toContain("No faults found");
    expect(json).not.toContain("Fault summary");
  });

  it("prints the recorded next test date on page one", () => {
    const t = sampleTest();
    t.nextTestDate = "2027-06-11";
    const doc = buildTestSummaryDoc(t);
    const content = doc.content as unknown[];
    const pageOne = JSON.stringify(content.slice(0, content.findIndex((n) => JSON.stringify(n).includes("Machine configuration"))));
    expect(pageOne).toContain("NEXT TEST DUE");
    expect(pageOne).toContain("11 June 2027");
    expect(pageOne).not.toContain("proposed");
  });

  it("prints a draft's proposed date, and none for a test signed off before the date existed", () => {
    const draft = sampleTest();
    draft.markedCompleteAt = null;
    const json = JSON.stringify(buildTestSummaryDoc(draft).content);
    expect(json).toContain("NEXT TEST DUE");
    expect(json).toContain("proposed");

    // A draft amendment carries the original test's date: settled, not proposed.
    const amending = { ...draft, supersedesId: "t0", version: 2, nextTestDate: "2027-06-11" };
    const amendJson = JSON.stringify(buildTestSummaryDoc(amending).content);
    expect(amendJson).toContain("11 June 2027");
    expect(amendJson).not.toContain("proposed");

    // Completed with no recorded date: nothing is invented.
    expect(JSON.stringify(buildTestSummaryDoc(sampleTest()).content)).not.toContain("NEXT TEST DUE");
  });

  it("anchors the compliance disclaimer, word for word, to the foot of page one", () => {
    // Requirements & Scope v1.1, section 7.3.
    expect(COMPLIANCE_DISCLAIMER).toBe(
      "This Machine Test may identify numerous hazards, however it in no way guarantees safety compliance for all or any hazard/s. " +
        "It is the farm owner’s responsibility to ensure that all hazards comply with WorkSafe and relevant NZ Safety Standard/s.",
    );
    const doc = buildTestSummaryDoc(sampleTest());
    const size = { width: 595.28, height: 841.89 };
    const background = doc.background as (page: number, size: object) => unknown;
    const pageOne = background(1, size) as { absolutePosition?: { y: number } }[];
    const box = pageOne.find((n) => JSON.stringify(n).includes("COMPLIANCE DISCLAIMER"))!;
    expect(JSON.stringify(box)).toContain("farm owner’s responsibility");
    // In the band the bottom margin reserves, so page one's content can never reach it.
    const [, , , bottom] = doc.pageMargins as number[];
    expect(box.absolutePosition!.y).toBeGreaterThanOrEqual(size.height - bottom);
    // Page one only, and not part of the flowing content.
    expect(JSON.stringify(background(2, size))).not.toContain("COMPLIANCE DISCLAIMER");
    expect(JSON.stringify(doc.content)).not.toContain("COMPLIANCE DISCLAIMER");
  });

  it("names the tester with their company, phone and registration so the farmer can call them", () => {
    const t = sampleTest();
    t.testedBy = { name: "Alan Tester", phone: "021 752 097", registrationNumber: "594", registrationExpiry: "2026-10-31" };
    const json = JSON.stringify(buildTestSummaryDoc(t, undefined, { companyName: "Sample Testing Co. Ltd" }).content);
    expect(json).toContain("TESTED BY");
    expect(json).toContain("Alan Tester");
    expect(json).toContain("Sample Testing Co. Ltd");
    expect(json).toContain("Phone 021 752 097");
    expect(json).toContain("NZMPTA registration 594 · expires 31/10/2026");
  });

  it("prefers the tester stamped on the test over any fallback, and leaves unknown lines off", () => {
    const t = sampleTest();
    t.testedBy = { name: "Original Tester" };
    const json = JSON.stringify(buildTestSummaryDoc(t, undefined, undefined, { name: "Someone Else" }).content);
    expect(json).toContain("Original Tester");
    expect(json).not.toContain("Someone Else");
    expect(json).not.toContain("Phone ");
    expect(json).not.toContain("NZMPTA registration");

    // Unstamped: the fallback names them.
    expect(JSON.stringify(buildTestSummaryDoc(sampleTest(), undefined, undefined, { name: "Fallback Tester" }).content))
      .toContain("Fallback Tester");
  });

  it("explains the severity ratings under the fault table, and only where there are faults", () => {
    const doc = buildTestSummaryDoc(sampleTest());
    const content = doc.content as unknown[];
    const pageOne = JSON.stringify(content.slice(0, content.findIndex((n) => JSON.stringify(n).includes("Machine configuration"))));
    expect(pageOne).toContain("SEVERITY RATINGS");
    for (const meaning of Object.values(SEVERITY_MEANING)) expect(pageOne).toContain(meaning);
    expect(pageOne.indexOf("SEVERITY RATINGS")).toBeGreaterThan(pageOne.indexOf("Oil Wicks Dirty"));

    const clean = sampleTest();
    clean.visualFaults = {};
    clean.readings = {};
    expect(JSON.stringify(buildTestSummaryDoc(clean).content)).not.toContain("SEVERITY RATINGS");
  });

  it("keeps page one to the farmer's essentials: no version notice, no machine line", () => {
    const t = sampleTest();
    t.version = 2;
    t.supersedesId = "t0";
    const content = buildTestSummaryDoc(t).content as unknown[];
    const config = content.findIndex((n) => JSON.stringify(n).includes("Machine configuration"));
    const pageOne = JSON.stringify(content.slice(0, config));
    expect(pageOne).toContain("Version 2"); // the title line says so
    expect(pageOne).not.toContain("supersedes an earlier completed test");
    expect(pageOne).not.toContain("MACHINE");
    // The configuration is still on page two.
    expect(JSON.stringify(content.slice(config))).toContain("Plant");
  });

  it("puts the annual-test reminder with the next test date on page one", () => {
    const t = sampleTest();
    t.nextTestDate = "2027-06-11";
    const doc = buildTestSummaryDoc(t);
    const content = doc.content as unknown[];
    const config = content.findIndex((n) => JSON.stringify(n).includes("Machine configuration"));
    const pageOne = JSON.stringify(content.slice(0, config));
    expect(pageOne).toContain(ANNUAL_TEST_NOTE);
    expect(pageOne.indexOf(ANNUAL_TEST_NOTE)).toBeGreaterThan(pageOne.indexOf("NEXT TEST DUE"));
    // Once, on page one: not repeated in the footer.
    const footer = doc.footer as (page: number, pages: number) => unknown;
    expect(JSON.stringify(footer(2, 4))).not.toContain(ANNUAL_TEST_NOTE);
  });

  it("puts the page number and copyright on every page", () => {
    const footer = buildTestSummaryDoc(sampleTest()).footer as (page: number, pages: number) => unknown;
    for (const page of [1, 2, 4]) {
      const json = JSON.stringify(footer(page, 4));
      expect(json).toContain(`Page ${page} of 4`);
      expect(json).toContain("New Zealand Milking and Pumping Trade Association");
    }
    expect(copyrightNotice(2026)).toBe("© 2026 New Zealand Milking and Pumping Trade Association. All rights reserved.");
  });

  it("prints the disclaimer on every report: clean, draft and migrated", () => {
    const clean = sampleTest();
    clean.visualFaults = {};
    clean.readings = {};
    const draft = { ...sampleTest(), markedCompleteAt: null };
    const migrated = { ...sampleTest(), recordedRecommendations: [], recordedVisualFaults: [], readonly: true };
    for (const t of [clean, draft, migrated]) {
      const background = buildTestSummaryDoc(t).background as (page: number, size: object) => unknown;
      expect(JSON.stringify(background(1, { width: 595.28, height: 841.89 }))).toContain("COMPLIANCE DISCLAIMER");
    }
  });

  it("marks a report generated before sign-off as a draft", () => {
    const t = sampleTest();
    t.markedCompleteAt = null;
    const json = JSON.stringify(buildTestSummaryDoc(t).content);
    expect(json).toContain("DRAFT");
    expect(json).toContain("Not yet signed off");
  });

  it("spells out the arrow the bundled font cannot draw", () => {
    // airlineDropRR is labelled "Drop receiver → regulator (4c)"; Roboto has no → glyph.
    const json = JSON.stringify(buildTestSummaryDoc(sampleTest()).content);
    expect(json).toContain("Drop receiver to regulator (4c)");
    expect(json).not.toContain("→");
  });

  it("keeps section headings off the foot of a page", () => {
    const { pageBreakBefore } = buildTestSummaryDoc(sampleTest());
    const at = (headlineLevel: number, verticalRatio: number) =>
      pageBreakBefore!({ headlineLevel, startPosition: { verticalRatio } } as never, {} as never);
    expect(at(1, 0.95)).toBe(true);
    expect(at(1, 0.5)).toBe(false);
    expect(at(2, 0.95)).toBe(true);
    expect(at(2, 0.88)).toBe(false);
  });

  it("prints every entered reading in the full report, under its test family", () => {
    const t = sampleTest();
    t.config = { ...t.config, vsdFitted: true, hasAcr: true, hasMilkMeters: true, hasTeatSprayer: true, hasBailGates: true, hasReleaserPump: true };
    const sections = allReadingSections(t.config);
    t.readings = Object.fromEntries(sections.map((s) => [s.readings[0].key, 1]));
    const json = JSON.stringify(buildTestSummaryDoc(t).content);
    for (const s of sections) expect(json).toContain(s.title);
    // Families in flowchart order, each under its own heading.
    const families = ["Vacuum tests (ISO 1–9)", "Airflow tests (ISO 10–12)", "Individual cluster tests (ISO 13)", "Pulsation & ancillary (ISO 14–15)", "Additional tests"];
    const at = families.map((f) => json.indexOf(f));
    expect(at.every((i) => i >= 0)).toBe(true);
    expect([...at].sort((a, b) => a - b)).toEqual(at);
  });
});

describe("printing some of the report", () => {
  const size = { width: 595.28, height: 841.89 };
  const only = (t: LocalTest, parts: ReportPart[]) => buildTestSummaryDoc(t, undefined, undefined, undefined, parts);
  const nodes = (doc: ReturnType<typeof buildTestSummaryDoc>) => doc.content as { id?: string; pageBreak?: string }[];

  it("offers the sections this test has something in, in report order", () => {
    const t = sampleTest();
    // ISO 1–9 readings, one pulsator row, the visual checks and the sign-off attestation — no
    // airflow, cluster or additional readings, no attachment, no amendments.
    expect(reportPartOptions(t).map((o) => o.part)).toEqual(["summary", "machine", "vacuum", "pulsation", "visual", "audit"]);

    t.readings = { ...t.readings, "add.airflowVacuumSystem": 900, "add.regulatorLoad": 1 };
    t.clusterRows = [{ id: "c1", unit: "4", values: { totalAirAdmission: "14" } }];
    t.pulsationPdf = { name: "analyser-export.pdf", base64: "JVBERi0=", size: 1234, attachedAt: t.updatedAt };
    t.amendments = [{ version: 2, amendedAt: "2026-07-06T00:00:00.000Z", baseVersion: 1, changes: [] }];
    const options = reportPartOptions(t);
    expect(options.map((o) => o.part)).toEqual([...REPORT_PARTS]);
    expect(options.find((o) => o.part === "analyser")?.hint).toContain("analyser-export.pdf");
    expect(options.find((o) => o.part === "audit")?.label).toBe("Attestations & amendment history");
    expect(options.find((o) => o.part === "pulsation")?.label).toBe("Pulsation & ancillary (ISO 14–15)");

    // A migrated test's report has no visual checks, so neither does its picker.
    const migrated = { ...sampleTest(), recordedRecommendations: [], recordedVisualFaults: [], readonly: true };
    expect(reportPartOptions(migrated).map((o) => o.part)).not.toContain("visual");
  });

  it("prints only the chosen sections, in report order, the working from page two", () => {
    const doc = only(sampleTest(), ["visual", "summary"]);
    const json = JSON.stringify(doc.content);
    expect(json).toContain("Fault summary");
    expect(json).toContain("Visual checks");
    for (const left of ["Machine configuration", "Vacuum tests", "Pulsator results", "Attestations"]) {
      expect(json).not.toContain(left);
    }
    // The visual checks are where the working starts, so they're what starts page two.
    const visual = nodes(doc).findIndex((n) => JSON.stringify(n).includes("Visual checks"));
    const breaksBefore = (pageNumber: number) =>
      doc.pageBreakBefore!({ id: nodes(doc)[visual].id, startPosition: { pageNumber, verticalRatio: 0.2 } } as never, {} as never);
    expect(breaksBefore(1)).toBe(true);
    expect(nodes(doc).some((n) => n.pageBreak)).toBe(false);
    expect(json.indexOf("Fault summary")).toBeLessThan(json.indexOf("Visual checks"));
  });

  it("opens with the first chosen section under the running header when the summary is left out", () => {
    const doc = only(sampleTest(), ["pulsation", "machine"]);
    expect(JSON.stringify(nodes(doc)[0])).toContain("Machine configuration");
    expect(nodes(doc).some((n) => n.pageBreak)).toBe(false);
    expect(JSON.stringify(doc.content)).not.toContain("NEXT TEST DUE");
    // Page one names the farm in the running header, and is laid out as a working page.
    const header = doc.header as (page: number, pages: number, size: unknown) => unknown;
    expect(JSON.stringify(header(1, 2, size))).toContain("Sunny Acres");
    const background = doc.background as (page: number, size: object) => unknown[];
    expect(background(1, size)).toHaveLength(1); // the disclaimer alone: no letterhead swirl
    const footer = doc.footer as (page: number, pages: number) => unknown;
    expect(JSON.stringify(footer(1, 2))).toContain("<svg"); // the working pages' tapered rule…
    const summaryFooter = buildTestSummaryDoc(sampleTest()).footer as typeof footer;
    expect(JSON.stringify(summaryFooter(1, 2))).not.toContain("<svg"); // …where the summary page has none
  });

  it("keeps the compliance disclaimer on page one of every print", () => {
    for (const parts of [["summary"], ["visual"], ["audit"], ["pulsation", "additional"]] as ReportPart[][]) {
      const background = only(sampleTest(), parts).background as (page: number, size: object) => unknown;
      expect(JSON.stringify(background(1, size))).toContain("COMPLIANCE DISCLAIMER");
      expect(JSON.stringify(background(2, size))).not.toContain("COMPLIANCE DISCLAIMER");
    }
  });

  it("doesn't leave page one blank when the amendment history prints first", () => {
    const t = sampleTest();
    t.attestations = [];
    t.amendments = [{ version: 2, amendedAt: "2026-07-06T00:00:00.000Z", baseVersion: 1, changes: [] }];
    const alone = nodes(only(t, ["audit"]));
    expect(JSON.stringify(alone[0])).toContain("Amendment history");
    expect(alone[0].pageBreak).toBeUndefined();
    // With the rest of the report it still starts a page of its own.
    const full = nodes(buildTestSummaryDoc(t));
    expect(full.find((n) => JSON.stringify(n).includes("Amendment history"))?.pageBreak).toBe("before");
  });

  it("prints each family's per-unit table with its own readings", () => {
    const t = sampleTest();
    t.readings = { ...t.readings, "puls.airlineStability": 3 };
    t.clusterRows = [{ id: "c1", unit: "4", values: { totalAirAdmission: "14" } }];
    const pulsation = JSON.stringify(only(t, ["pulsation"]).content);
    expect(pulsation).toContain("Pulsator results");
    expect(pulsation).toContain("Pulsator airline stability");
    expect(pulsation).not.toContain("Cluster results");
    const cluster = JSON.stringify(only(t, ["cluster"]).content);
    expect(cluster).toContain("Cluster results");
    expect(cluster).not.toContain("Pulsator results");
  });

  it("leaves the analyser note off when the analyser report isn't printed", () => {
    const t = sampleTest();
    t.pulsationPdf = { name: "analyser-export.pdf", base64: "JVBERi0=", size: 1234, attachedAt: t.updatedAt };
    expect(JSON.stringify(only(t, ["summary", "visual"]).content)).not.toContain("analyser-export.pdf");
    expect(JSON.stringify(only(t, ["summary", "analyser"]).content)).toContain("appended to this document");
  });

  it("prints the summary rather than a blank document when nothing printable was chosen", () => {
    for (const parts of [[], ["airflow"]] as ReportPart[][]) {
      const json = JSON.stringify(only(sampleTest(), parts).content);
      expect(json).toContain("Fault summary");
      expect(json).not.toContain("Machine configuration");
    }
  });
});

// Laid out for real with the app's pdfmake build, so these see where pages actually fall.
describe("page two, laid out", () => {
  /** How many pages the document takes. pdfmake writes into the definition as it lays it out, so
   * each layout needs a freshly built one — as each download builds its own. */
  async function pageCount(doc: TDocumentDefinitions): Promise<number> {
    const [pdfMakeModule, vfsModule, { PDFDocument }] = await Promise.all([
      import("pdfmake/build/pdfmake"),
      import("pdfmake/build/vfs_fonts"),
      import("pdf-lib"),
    ]);
    const pdfMake = ((pdfMakeModule as { default?: unknown }).default ?? pdfMakeModule) as {
      addVirtualFileSystem(vfs: unknown): void;
      createPdf(doc: TDocumentDefinitions): { getBuffer(): Promise<Uint8Array> };
    };
    pdfMake.addVirtualFileSystem((vfsModule as { default?: unknown }).default ?? vfsModule);
    return (await PDFDocument.load(await pdfMake.createPdf(doc).getBuffer())).getPageCount();
  }
  const summaryAndMachine = (t: LocalTest) => buildTestSummaryDoc(t, undefined, undefined, undefined, ["summary", "machine"]);

  it("starts the working on page two when the summary fits page one", async () => {
    expect(await pageCount(summaryAndMachine(sampleTest()))).toBe(2);
    // The machine configuration would otherwise have fitted under it on page one.
    expect(await pageCount({ ...summaryAndMachine(sampleTest()), pageBreakBefore: () => false })).toBe(1);
  });

  it("carries the working on under a summary that has run onto page two", async () => {
    const t = sampleTest();
    t.notes = Array.from({ length: 36 }, (_, i) => `${i + 1}. Adjusted and rechecked on site.`).join("\n");
    // The comments spill onto page two and the machine configuration follows them there.
    expect(await pageCount(summaryAndMachine(t))).toBe(2);
    // A fixed break before it — the old layout — pushed it to page three, stranding the comments.
    const doc = summaryAndMachine(t);
    const fixedBreak = {
      ...doc,
      content: (doc.content as { id?: string }[]).map((n) => (n.id ? { ...n, pageBreak: "before" } : n)),
    } as TDocumentDefinitions;
    expect(await pageCount(fixedBreak)).toBe(3);
  });
});

describe("reportFileName", () => {
  it("marks a print of only some sections, so it can't pass for the full report", () => {
    const t = sampleTest();
    t.markedCompleteAt = "2026-09-14T20:41:48.000Z";
    expect(reportFileName(t)).toBe("Test Summary - Sunny Acres - 2026-09-15.pdf");
    expect(reportFileName(t, ["summary"])).toBe("Test Summary - Sunny Acres - 2026-09-15 - selected sections.pdf");
    // Every section the test has, chosen one by one, is the full report.
    expect(reportFileName(t, reportPartOptions(t).map((o) => o.part))).toBe("Test Summary - Sunny Acres - 2026-09-15.pdf");
  });
});
