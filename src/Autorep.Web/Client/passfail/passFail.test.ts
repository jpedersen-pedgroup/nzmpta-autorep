import { describe, it, expect } from "vitest";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";
import { evaluate, type PassFailRule, type PassFailVerdict } from "./passFail";

interface RuleCase {
  value: number | null;
  expected: PassFailVerdict;
}
interface RuleFixture {
  name: string;
  rule: PassFailRule;
  cases: RuleCase[];
}

const fixturesPath = join(
  dirname(fileURLToPath(import.meta.url)),
  "../../../../tests/fixtures/passfail/cases.json",
);
const fixtures = JSON.parse(readFileSync(fixturesPath, "utf8")) as RuleFixture[];

describe("PassFailCalculator (TS) matches the shared fixtures", () => {
  expect(fixtures.length).toBeGreaterThan(0);

  for (const f of fixtures) {
    it(f.name, () => {
      for (const c of f.cases) {
        expect(evaluate(c.value, f.rule), `${f.name} @ ${c.value}`).toBe(c.expected);
      }
    });
  }
});
