import { describe, it, expect } from "vitest";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";
import { aggregate, type FaultInput } from "./faultAggregator";

interface ExpectedGroup {
  component: string;
  severity: string;
  count: number;
}
interface Fixture {
  name: string;
  inputs: FaultInput[];
  expectedGroups: ExpectedGroup[];
  critical: number;
  major: number;
  minor: number;
  total: number;
}

const fixturesPath = join(
  dirname(fileURLToPath(import.meta.url)),
  "../../../../tests/fixtures/faults/cases.json",
);
const fixtures = JSON.parse(readFileSync(fixturesPath, "utf8")) as Fixture[];

describe("FaultAggregator (TS) matches the shared fixtures", () => {
  for (const fx of fixtures) {
    it(fx.name, () => {
      const s = aggregate(fx.inputs);
      expect(
        s.groups.map((g) => ({ component: g.component, severity: g.severity, count: g.faults.length })),
      ).toEqual(fx.expectedGroups);
      expect([s.critical, s.major, s.minor, s.total]).toEqual([fx.critical, fx.major, fx.minor, fx.total]);
    });
  }
});
