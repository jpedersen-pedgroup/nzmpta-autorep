import { describe, it, expect } from "vitest";
import { oemPumpCapacity, vacuumPumpFor } from "./standardsData";

describe("vacuumPumpFor (legacy VPModel curve)", () => {
  it("finds the catalogue row for a make and model", () => {
    const pump = vacuumPumpFor("De Laval", "DVP1600");
    expect(pump).not.toBeNull();
    expect(pump!.airFlow).toBe(1.15);
    expect([pump!.minRpm, pump!.maxRpm]).toEqual([275, 1400]);
  });

  it("ignores case and stray spacing, because the make may have been typed", () => {
    expect(vacuumPumpFor("de laval", "dvp1600")?.model).toBe("DVP1600");
    expect(vacuumPumpFor("De  Laval ", " DVP1600")?.model).toBe("DVP1600");
  });

  it("has nothing for a pump outside the catalogue or a half-filled row", () => {
    expect(vacuumPumpFor("Acme", "Hyperpump")).toBeNull();
    expect(vacuumPumpFor("De Laval", null)).toBeNull();
    expect(vacuumPumpFor(null, "DVP1600")).toBeNull();
  });

  it("reproduces the pump's nameplate capacity at its catalogue top speed", () => {
    // The curve is linear: airFlow is L/min per rpm, so a DVP1600 at its 1400 rpm ceiling is the
    // 1600-class figure its name implies, and an LVP3000 lands on 3000.
    expect(Math.round(oemPumpCapacity(vacuumPumpFor("De Laval", "DVP1600")!, 1400))).toBe(1610);
    expect(Math.round(oemPumpCapacity(vacuumPumpFor("De Laval", "LVP3000")!, 2650))).toBe(3000);
  });
});
