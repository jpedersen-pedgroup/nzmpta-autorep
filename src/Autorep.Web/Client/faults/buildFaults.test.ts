import { describe, it, expect } from "vitest";
import { buildFaultInputs, RATE_BAND_FAULT_KEY, RATIO_BAND_FAULT_KEY, REGULATOR_FAULT_KEY } from "./buildFaults";
import { defaultMachineConfiguration, type MachineConfiguration } from "../wizard/types";
import type { LocalTest, MeasurementRow } from "../db/testStore";

// Dairymaster "Electronic (Simultaneous)": rate 59–61 ppm, ratio 59–69% (reference/pulsatorBands.json).
const BANDED_MODEL = "Electronic (Simultaneous)";

function makeTest(config: Partial<MachineConfiguration> = {}, pulsatorRows?: MeasurementRow[], recommendations: Record<string, string> = {}): LocalTest {
  return {
    id: "t1",
    farmName: "Farm",
    config: { ...defaultMachineConfiguration(), clusterCount: 20, ...config },
    currentStep: "FaultSummary",
    visualFaults: {},
    attestations: [],
    readings: {},
    recommendations,
    dataFields: {},
    pulsatorRows,
    createdAt: "2026-10-19T00:00:00.000Z",
    updatedAt: "2026-10-19T00:00:00.000Z",
    markedCompleteAt: null,
    syncState: "local-only",
  };
}

const row = (unit: string, rate: string, ratioFront: string, ratioBack = ""): MeasurementRow =>
  ({ id: unit, unit, values: { rate, ratioFront, ratioBack } });

describe("buildFaultInputs · machine checks", () => {
  it("lists regulators the tester judged not correct or not big enough, as a Major fault", () => {
    const fault = buildFaultInputs(makeTest({ regulatorsSuitable: false })).find((f) => f.key === REGULATOR_FAULT_KEY);
    expect(fault).toMatchObject({ component: "Regulator", severity: "Major" });
    expect(fault?.recommendation).toBeTruthy();
  });

  it("raises nothing for a Yes, or a question left unanswered", () => {
    expect(buildFaultInputs(makeTest({ regulatorsSuitable: true }))).toEqual([]);
    expect(buildFaultInputs(makeTest({ regulatorsSuitable: null }))).toEqual([]);
  });

  it("lists pulsators outside their model's rate or ratio band", () => {
    const faults = buildFaultInputs(makeTest({ pulsatorModel: BANDED_MODEL }, [row("1", "58", "60"), row("2", "60", "72")]));
    const rate = faults.find((f) => f.key === RATE_BAND_FAULT_KEY);
    const ratio = faults.find((f) => f.key === RATIO_BAND_FAULT_KEY);
    expect(rate?.description).toBe("Pulsator rate 58–60 ppm is outside the model band of 59–61 ppm");
    expect(ratio?.description).toBe("Pulsator ratio 60–72% is outside the model band of 59–69%");
    expect([rate?.severity, ratio?.severity]).toEqual(["Major", "Major"]);

    // One unit recorded: its value, not a range from it to itself.
    const one = buildFaultInputs(makeTest({ pulsatorModel: BANDED_MODEL }, [row("3", "58", "62")])).find((f) => f.key === RATE_BAND_FAULT_KEY);
    expect(one?.description).toBe("Pulsator rate 58 ppm is outside the model band of 59–61 ppm");
  });

  it("raises nothing inside the bands, for an unknown model, or with no units recorded", () => {
    expect(buildFaultInputs(makeTest({ pulsatorModel: BANDED_MODEL }, [row("1", "60", "62")]))).toEqual([]);
    expect(buildFaultInputs(makeTest({ pulsatorModel: "Not a catalogue model" }, [row("1", "40", "90")]))).toEqual([]);
    expect(buildFaultInputs(makeTest({ pulsatorModel: BANDED_MODEL }, []))).toEqual([]);
  });

  it("judges a signed-off test against the band pinned at sign-off, not today's spec", () => {
    // Today's band for the model is 59–61 ppm; this test was signed off against 55–65.
    const pinned = {
      pulsator: {
        brand: "Dairymaster", name: BANDED_MODEL,
        spec: { rateMin: 55, rateMax: 65, ratioMin: 59, ratioMax: 69, phaseB: null, phaseD: null, maxChamberVacuum: null, recommendedRate: null, recommendedRatio: null },
      },
    };
    const t = makeTest({ pulsatorModel: BANDED_MODEL, pulsatorBrand: "Dairymaster", oemSpecs: pinned }, [row("3", "58", "62")]);
    expect(buildFaultInputs(t).find((f) => f.key === RATE_BAND_FAULT_KEY)).toBeUndefined();
    // Without the pin, the same unit is outside today's band.
    expect(buildFaultInputs({ ...t, config: { ...t.config, oemSpecs: null } }).find((f) => f.key === RATE_BAND_FAULT_KEY)).toBeDefined();
  });

  it("keeps the tester's own recommendation for a machine check", () => {
    const fault = buildFaultInputs(makeTest({ regulatorsSuitable: false }, undefined, { [REGULATOR_FAULT_KEY]: "Add a second regulator." }))
      .find((f) => f.key === REGULATOR_FAULT_KEY);
    expect(fault?.recommendation).toBe("Add a second regulator.");
  });
});
