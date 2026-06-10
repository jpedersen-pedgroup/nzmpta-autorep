import { describe, it, expect } from "vitest";
import { buildTestSummaryDoc } from "./testSummaryPdf";
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

describe("buildTestSummaryDoc", () => {
  it("builds a document with the core sections and farm name", () => {
    const doc = buildTestSummaryDoc(sampleTest());
    const json = JSON.stringify(doc.content);
    expect(json).toContain("Milking Machine Test Summary");
    expect(json).toContain("Sunny Acres");
    expect(json).toContain("Fault summary");
    expect(json).toContain("Numerical test results");
    expect(json).toContain("Pulsator test results");
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

  it("notes the appended pulsation PDF when one is attached", () => {
    const t = sampleTest();
    t.pulsationPdf = { name: "analyser-export.pdf", base64: "JVBERi0=", size: 1234, attachedAt: t.updatedAt };
    const json = JSON.stringify(buildTestSummaryDoc(t).content);
    expect(json).toContain("Attachments");
    expect(json).toContain("analyser-export.pdf");
    expect(json).toContain("appended to this document");
  });
});
