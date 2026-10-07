import { describe, expect, it } from "vitest";
import type { LocalTest } from "../db/testStore";
import { applyCheckAll, isCheckItem, preStartSections, runningSectionsFor } from "../wizard/visualChecklist";
import { runningSectionKeys } from "../wizard/wizardProgress";
import { defaultMachineConfiguration, type ChecklistAttestation, type MachineConfiguration } from "../wizard/types";
import { bulkConfirmedSections, checklistVerification } from "./attestations";

function makeTest(over: Partial<LocalTest> = {}, config: Partial<MachineConfiguration> = {}): LocalTest {
  return {
    id: "t1",
    farmName: "Tievoli Farms Ltd",
    config: { ...defaultMachineConfiguration(), clusterCount: 24, ...config },
    currentStep: "ReviewSignOff",
    visualFaults: {},
    attestations: [],
    readings: {},
    recommendations: {},
    dataFields: {},
    createdAt: "2026-08-01T00:00:00.000Z",
    updatedAt: "2026-08-01T00:00:00.000Z",
    markedCompleteAt: "2026-08-01T03:00:00.000Z",
    syncState: "uploaded",
    ...over,
  };
}

const attest = (step: ChecklistAttestation["step"], section: string | undefined, at: string): ChecklistAttestation => ({
  step,
  ...(section ? { section } : {}),
  attestedAt: at,
  text: "I have inspected all items on this page and confirm they have been seen, tested and are in order.",
});

const [vacuumPump] = preStartSections(false);
const checks = vacuumPump.items.filter(isCheckItem);

describe("checklistVerification (PRD story 69)", () => {
  it("marks a bulk-confirmed section's OK items as bulk, and its faults as set individually", () => {
    const test = makeTest({
      visualFaults: {
        ...applyCheckAll([vacuumPump], { "vp.wick": { status: "fault", severity: "Minor", observation: "Oil Wicks Dirty" } }),
      },
      attestations: [attest("VisualFaultsPreStart", vacuumPump.key, "2026-08-01T01:00:00.000Z")],
    });

    const [section] = checklistVerification(test);
    expect(section.step).toBe("VisualFaultsPreStart");
    expect(section.key).toBe("VacuumPump");
    expect(section.attestation?.attestedAt).toBe("2026-08-01T01:00:00.000Z");
    // Data-capture fields (belt size) aren't checks, so they're not listed.
    expect(section.items.map((i) => i.key)).toEqual(checks.map((i) => i.key));
    expect(section.items.find((i) => i.key === "vp.wick")).toEqual({
      key: "vp.wick",
      label: "Wick condition",
      status: "fault",
      verified: "individual",
      detail: "Minor: Oil Wicks Dirty",
    });
    expect(section.items.filter((i) => i.key !== "vp.wick").every((i) => i.status === "ok" && i.verified === "bulk")).toBe(true);
  });

  it("marks a section checked by hand as individual, with blanks left unchecked", () => {
    const test = makeTest({ visualFaults: { "vp.oilWater": { status: "ok" }, "vp.belt": { status: "fault" } } });

    const [section] = checklistVerification(test);
    expect(section.attestation).toBeNull();
    const byKey = Object.fromEntries(section.items.map((i) => [i.key, i]));
    expect(byKey["vp.oilWater"].verified).toBe("individual");
    expect(byKey["vp.belt"]).toMatchObject({ status: "fault", verified: "individual", detail: "Major" });
    expect(byKey["vp.guards"]).toMatchObject({ status: null, verified: "unchecked" });
  });

  it("leaves out sections nothing was recorded in", () => {
    expect(checklistVerification(makeTest())).toEqual([]);

    const releaser = preStartSections(true);
    const test = makeTest({ visualFaults: { "rmp.belt": { status: "ok" } } }, { hasReleaserPump: true });
    expect(checklistVerification(test).map((s) => s.key)).toEqual([releaser[1].key]);
  });

  it("covers the running sections this machine has, under the running step", () => {
    const config = { ...defaultMachineConfiguration(), clusterCount: 24 };
    const [first] = runningSectionsFor(runningSectionKeys(config));
    const test = makeTest({
      visualFaults: applyCheckAll([first], {}),
      attestations: [attest("VisualFaultsRunning", first.key, "2026-08-01T02:00:00.000Z")],
    });

    const sections = checklistVerification(test);
    expect(sections).toHaveLength(1);
    expect(sections[0]).toMatchObject({ step: "VisualFaultsRunning", key: first.key, title: first.title });
    expect(sections[0].items.every((i) => i.verified === "bulk")).toBe(true);
  });

  it("matches an attestation to its own step and section only", () => {
    // Attested on the running step under the same key: not the pre-start section's attestation.
    const test = makeTest({
      visualFaults: { "vp.wick": { status: "ok" } },
      attestations: [attest("VisualFaultsRunning", vacuumPump.key, "2026-08-01T02:00:00.000Z")],
    });
    const [section] = checklistVerification(test);
    expect(section.attestation).toBeNull();
    expect(section.items.find((i) => i.key === "vp.wick")?.verified).toBe("individual");
  });

  it("uses the latest attestation when a section was confirmed twice", () => {
    const test = makeTest({
      visualFaults: applyCheckAll([vacuumPump], {}),
      attestations: [
        attest("VisualFaultsPreStart", vacuumPump.key, "2026-08-01T01:00:00.000Z"),
        attest("VisualFaultsPreStart", vacuumPump.key, "2026-08-01T01:30:00.000Z"),
      ],
    });
    expect(checklistVerification(test)[0].attestation?.attestedAt).toBe("2026-08-01T01:30:00.000Z");
  });

  it("reads an attestation that names no section as covering its whole step", () => {
    const test = makeTest({
      visualFaults: applyCheckAll(preStartSections(true), {}),
      attestations: [attest("VisualFaultsPreStart", undefined, "2026-08-01T01:00:00.000Z")],
    }, { hasReleaserPump: true });

    const sections = checklistVerification(test);
    expect(sections.map((s) => s.key)).toEqual(preStartSections(true).map((s) => s.key));
    expect(sections.every((s) => s.attestation !== null && s.items.every((i) => i.verified === "bulk"))).toBe(true);
  });
});

describe("bulkConfirmedSections", () => {
  it("counts each checklist section confirmed in bulk once, and not the sign-off declaration", () => {
    expect(
      bulkConfirmedSections([
        attest("VisualFaultsPreStart", "VacuumPump", "2026-08-01T01:00:00.000Z"),
        attest("VisualFaultsPreStart", "VacuumPump", "2026-08-01T01:30:00.000Z"),
        attest("VisualFaultsRunning", "MainAirline", "2026-08-01T02:00:00.000Z"),
        attest("ReviewSignOff", undefined, "2026-08-01T03:00:00.000Z"),
      ]),
    ).toBe(2);
    expect(bulkConfirmedSections([attest("ReviewSignOff", undefined, "2026-08-01T03:00:00.000Z")])).toBe(0);
  });
});
