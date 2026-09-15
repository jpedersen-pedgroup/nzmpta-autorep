import { describe, it, expect } from "vitest";
import { buildTestSummaryDoc, reportDateStamp } from "./testSummaryPdf";
import { defaultMachineConfiguration } from "../wizard/types";
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
    expect(json).toContain("airflow: 27/01/2027");
    expect(json).not.toContain("01/01/2030");
  });

  it("falls back to the tester's profile when the test has no stamp yet (pre-sign-off preview)", () => {
    const t = sampleTest();
    const json = JSON.stringify(
      buildTestSummaryDoc(t, { airFlowMeters: "2027-03-04", vacuumGauges: "2027-05-06" }).content,
    );
    expect(json).toContain("airflow: 04/03/2027");
    expect(json).toContain("vacuum: 06/05/2027");
    expect(json).toContain("pulsator: —");
  });

  it("shows an em dash when neither a stamp nor a profile date exists", () => {
    const json = JSON.stringify(buildTestSummaryDoc(sampleTest()).content);
    expect(json).toContain("airflow: — · pulsator: — · vacuum: —");
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
    expect(JSON.stringify(buildTestSummaryDoc(t).content)).toContain("Completed: 15/09/2026");
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
    expect(json).toContain("Numerical test results");
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
    expect(json).toContain("Amendment history section"); // banner points at the appendix
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

  it("prints the configuration with display names, not enum names", () => {
    const json = JSON.stringify(buildTestSummaryDoc(sampleTest()).content);
    expect(json).toContain("Herringbone (lowline)");
    expect(json).toContain("Oil lubricated");
    expect(json).not.toContain("HerringboneLowline");
    expect(json).not.toContain("OilLubricated");
  });

  it("prints general comments under the fault summary, and only when there are some", () => {
    const t = sampleTest();
    t.notes = "Regulation undershoot was excessive — VSD settings adjusted at time of test.";
    const json = JSON.stringify(buildTestSummaryDoc(t).content);
    expect(json).toContain("General comments");
    expect(json).toContain("VSD settings adjusted");
    // The comments follow the fault table, ahead of the numbers.
    expect(json.indexOf("General comments")).toBeGreaterThan(json.indexOf("Fault summary"));
    expect(json.indexOf("General comments")).toBeLessThan(json.indexOf("Numerical test results"));

    expect(JSON.stringify(buildTestSummaryDoc(sampleTest()).content)).not.toContain("General comments");
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
