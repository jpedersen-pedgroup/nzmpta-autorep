import { describe, it, expect } from "vitest";
import { readTypedNumber } from "./numericEntry";

describe("readTypedNumber", () => {
  it("keeps the reading while the decimal point is being typed", () => {
    // Chromium sanitises "41." to "" and flags badInput. Clearing the reading here, then writing
    // the old value back, is what entered 41.5 as 541 on a real test.
    expect(readTypedNumber("", true, "41")).toBeUndefined();
  });

  it("stores a decimal once the text parses", () => {
    expect(readTypedNumber("41.5", false, "41")).toBe(41.5);
    expect(readTypedNumber("41,5", false, "41")).toBe(41.5);
    expect(readTypedNumber("-0.5", false, "")).toBe(-0.5);
    expect(readTypedNumber("0", false, "")).toBe(0);
  });

  it("leaves the store alone while a trailing zero is typed", () => {
    // "41.0" parses to 41; storing that would strip the "0" and block 41.05.
    expect(readTypedNumber("41.0", false, "41")).toBeUndefined();
    expect(readTypedNumber("41.05", false, "41")).toBe(41.05);
  });

  it("clears the reading when the tester empties the field", () => {
    expect(readTypedNumber("", false, "41")).toBeNull();
    expect(readTypedNumber("", false, "")).toBeUndefined();
  });
});
