import { describe, it, expect } from "vitest";
import {
  daysUntil,
  describeStatus,
  equipmentStatuses,
  formatDisplayDate,
  stateFor,
  todayIso,
  worstState,
  DUE_SOON_DAYS,
} from "./status";

const TODAY = "2026-07-22";

describe("stateFor", () => {
  it("is unknown when no date is recorded", () => {
    expect(stateFor(null, TODAY)).toBe("unknown");
    expect(stateFor(undefined, TODAY)).toBe("unknown");
    expect(stateFor("", TODAY)).toBe("unknown");
    expect(stateFor("not-a-date", TODAY)).toBe("unknown");
  });

  it("is ok when expiry is more than 6 weeks away", () => {
    expect(stateFor("2026-09-03", TODAY)).toBe("ok"); // 43 days out
    expect(stateFor("2027-07-22", TODAY)).toBe("ok");
  });

  it("enters the renewal window exactly 6 weeks (42 days) out", () => {
    expect(daysUntil("2026-09-02", TODAY)).toBe(DUE_SOON_DAYS);
    expect(stateFor("2026-09-02", TODAY)).toBe("due"); // 42 days out
    expect(stateFor("2026-08-01", TODAY)).toBe("due");
  });

  it("stays valid through the expiry date itself, expired the day after", () => {
    expect(stateFor("2026-07-22", TODAY)).toBe("due"); // expires today — still usable
    expect(stateFor("2026-07-21", TODAY)).toBe("expired");
    expect(stateFor("2020-01-01", TODAY)).toBe("expired");
  });
});

describe("worstState", () => {
  it("expired outranks due outranks ok", () => {
    expect(worstState({ airFlowMeters: "2020-01-01", pulsatorTesters: "2027-01-01" }, TODAY)).toBe("expired");
    expect(worstState({ airFlowMeters: "2026-08-01", pulsatorTesters: "2027-01-01" }, TODAY)).toBe("due");
    expect(worstState({ airFlowMeters: "2027-01-01" }, TODAY)).toBe("ok");
  });

  it("is unknown only when nothing is recorded (no false alarms pre-rollout)", () => {
    expect(worstState({}, TODAY)).toBe("unknown");
    expect(worstState({ airFlowMeters: null, pulsatorTesters: null, vacuumGauges: null }, TODAY)).toBe("unknown");
  });
});

describe("equipmentStatuses / describeStatus", () => {
  it("labels all three instruments in order with day counts", () => {
    const statuses = equipmentStatuses(
      { airFlowMeters: "2026-07-25", pulsatorTesters: "2026-07-01", vacuumGauges: null },
      TODAY,
    );
    expect(statuses.map((s) => s.label)).toEqual(["Air-flow meters", "Pulsator testers", "Vacuum gauges"]);
    expect(statuses[0]).toMatchObject({ state: "due", days: 3 });
    expect(statuses[1]).toMatchObject({ state: "expired", days: -21 });
    expect(statuses[2]).toMatchObject({ state: "unknown", days: null });
  });

  it("phrases spans in days under two weeks, weeks beyond", () => {
    const at = (date: string | null) => equipmentStatuses({ airFlowMeters: date }, TODAY)[0];
    expect(describeStatus(at("2026-07-22"))).toBe("expires today");
    expect(describeStatus(at("2026-07-23"))).toBe("expires in 1 day");
    expect(describeStatus(at("2026-08-04"))).toBe("expires in 13 days");
    expect(describeStatus(at("2026-08-05"))).toBe("expires in 2 weeks");
    expect(describeStatus(at("2026-07-21"))).toBe("expired 1 day ago");
    expect(describeStatus(at("2026-07-01"))).toBe("expired 3 weeks ago");
    expect(describeStatus(at("2027-07-01"))).toBe("expires 01/07/2027");
    expect(describeStatus(at(null))).toBe("no date recorded");
  });
});

describe("date helpers", () => {
  it("todayIso uses the local calendar date", () => {
    expect(todayIso(new Date(2026, 6, 22, 23, 30))).toBe("2026-07-22");
    expect(todayIso(new Date(2026, 0, 1, 0, 5))).toBe("2026-01-01");
  });

  it("formatDisplayDate renders dd/mm/yyyy", () => {
    expect(formatDisplayDate("2027-01-27")).toBe("27/01/2027");
  });
});
