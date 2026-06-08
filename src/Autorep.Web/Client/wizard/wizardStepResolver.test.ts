import { describe, it, expect } from "vitest";
import { readdirSync, readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";
import { resolveWizard } from "./wizardStepResolver";
import { defaultMachineConfiguration, type MachineConfiguration } from "./types";

// The TS resolver is validated against the SAME JSON fixtures as the .NET xUnit suite
// (tests/fixtures/wizard), so the two implementations can't drift apart.
interface Fixture {
  name: string;
  config: Partial<MachineConfiguration>;
  expectedSteps: string[];
  optionalSteps: string[];
  shortTest: boolean;
  expectedSections?: Record<string, string[]>;
}

const fixturesDir = join(
  dirname(fileURLToPath(import.meta.url)),
  "../../../../tests/fixtures/wizard",
);
const fixtureFiles = readdirSync(fixturesDir).filter((f) => f.endsWith(".json"));

describe("WizardStepResolver (TS) matches the shared fixtures", () => {
  expect(fixtureFiles.length).toBeGreaterThan(0);

  for (const file of fixtureFiles) {
    const fx = JSON.parse(readFileSync(join(fixturesDir, file), "utf8")) as Fixture;

    it(fx.name ?? file, () => {
      const config = { ...defaultMachineConfiguration(), ...fx.config };
      const plan = resolveWizard(config);

      expect(plan.steps.map((s) => s.step)).toEqual(fx.expectedSteps);
      expect(plan.steps.filter((s) => s.isOptional).map((s) => s.step).sort()).toEqual(
        [...fx.optionalSteps].sort(),
      );
      expect(plan.isShortTest).toBe(fx.shortTest);

      for (const [stepName, expected] of Object.entries(fx.expectedSections ?? {})) {
        const resolved = plan.steps.find((s) => s.step === stepName);
        expect(resolved, `step ${stepName} present`).toBeDefined();
        expect(resolved!.sections).toEqual(expected);
      }
    });
  }
});

describe("WizardStepResolver (TS) rules", () => {
  it("omits MinPumpSpeedVacuum unless a VSD is fitted", () => {
    const base = defaultMachineConfiguration();
    const noVsd = resolveWizard(base).steps.find((s) => s.step === "TestRecord")!;
    const withVsd = resolveWizard({ ...base, vsdFitted: true }).steps.find(
      (s) => s.step === "TestRecord",
    )!;
    expect(noVsd.sections).not.toContain("MinPumpSpeedVacuum");
    expect(withVsd.sections).toContain("MinPumpSpeedVacuum");
  });

  it("shows the ACR section only when ACRs are present", () => {
    const base = defaultMachineConfiguration();
    const withAcr = resolveWizard({ ...base, hasAcr: true }).steps.find(
      (s) => s.step === "AdditionalTests",
    )!;
    const noAcr = resolveWizard(base).steps.find((s) => s.step === "AdditionalTests")!;
    expect(withAcr.sections).toContain("AcrConsumption");
    expect(noAcr.sections).not.toContain("AcrConsumption");
  });

  it("flags a short test when ISO ports are unavailable", () => {
    const base = defaultMachineConfiguration();
    expect(resolveWizard(base).isShortTest).toBe(false);
    expect(resolveWizard({ ...base, isoPortsAvailable: false }).isShortTest).toBe(true);
  });
});
