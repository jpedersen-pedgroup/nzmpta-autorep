import { describe, it, expect, afterEach } from "vitest";
import {
  regulatorTypeOptions,
  releaserPumpMakeOptions,
  releaserPumpModelOptionsForMake,
  vacuumPumpMakeOptions,
  vacuumPumpModelOptionsForMake,
} from "./lookups";
import { applyEquipmentOverrides, clearEquipmentOverrides } from "./catalogOverrides";

afterEach(() => clearEquipmentOverrides());

describe("vacuum and releaser pump catalogues", () => {
  it("uses the bundled legacy catalogue before anything has synced", () => {
    expect(vacuumPumpMakeOptions()).toContain("MASPORT");
    expect(vacuumPumpModelOptionsForMake("De Laval")).toContain("DVP1600");
    expect(releaserPumpMakeOptions()).toContain("DELAVAL");
    expect(releaserPumpModelOptionsForMake("READ").length).toBeGreaterThan(0);
  });

  it("matches the make however it was spelled or typed", () => {
    // The make can be typed by hand, and the legacy catalogues are not consistent about case.
    expect(vacuumPumpModelOptionsForMake("de laval")).toContain("DVP1600");
    expect(vacuumPumpModelOptionsForMake("  De  Laval  ")).toContain("DVP1600");
    expect(vacuumPumpModelOptionsForMake("Acme")).toEqual([]);
    expect(vacuumPumpModelOptionsForMake(null)).toEqual([]);
  });

  it("lets a synced catalogue replace the bundled one, for that catalogue only", () => {
    applyEquipmentOverrides([{ type: "VacuumPump", name: "Hyperpump 9000", brand: "Acme" }]);
    expect(vacuumPumpMakeOptions()).toEqual(["Acme"]);
    expect(vacuumPumpModelOptionsForMake("Acme")).toEqual(["Hyperpump 9000"]);
    expect(releaserPumpMakeOptions()).toContain("DELAVAL");
  });

  it("offers no regulator types until an admin adds some", () => {
    expect(regulatorTypeOptions()).toEqual([]);
    applyEquipmentOverrides([{ type: "Regulator", name: "Servo" }]);
    expect(regulatorTypeOptions()).toEqual(["Servo"]);
  });
});
