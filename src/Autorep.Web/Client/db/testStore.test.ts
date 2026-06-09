import "fake-indexeddb/auto";
import { describe, it, expect } from "vitest";
import { allTests, deleteTest, getTest, putTest, type LocalTest } from "./testStore";
import { defaultMachineConfiguration } from "../wizard/types";

function sample(id: string, farmName = "Sunny Acres"): LocalTest {
  const now = "2026-06-08T00:00:00.000Z";
  return {
    id,
    farmName,
    config: defaultMachineConfiguration(),
    currentStep: "Setup",
    visualFaults: {},
    attestations: [],
    readings: {},
    recommendations: {},
    createdAt: now,
    updatedAt: now,
    markedCompleteAt: null,
    syncState: "local-only",
  };
}

describe("testStore (IndexedDB)", () => {
  it("round-trips a local test", async () => {
    await putTest(sample("t1"));
    const got = await getTest("t1");
    expect(got?.farmName).toBe("Sunny Acres");
    expect(got?.config.plantType).toBe("HerringboneLowline");
    expect(got?.syncState).toBe("local-only");
  });

  it("updates an existing test in place", async () => {
    await putTest(sample("t2"));
    await putTest({ ...sample("t2", "Renamed Farm"), config: { ...defaultMachineConfiguration(), hasAcr: true } });
    const got = await getTest("t2");
    expect(got?.farmName).toBe("Renamed Farm");
    expect(got?.config.hasAcr).toBe(true);
  });

  it("lists and deletes", async () => {
    await putTest(sample("t3"));
    expect((await allTests()).some((t) => t.id === "t3")).toBe(true);
    await deleteTest("t3");
    expect(await getTest("t3")).toBeUndefined();
  });
});
