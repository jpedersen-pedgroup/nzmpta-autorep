import { describe, expect, it } from "vitest";
import { adaptLegacyReadings, decodeReadingVerdict, decodeStatusVerdict } from "./legacyAdapter";

describe("legacy rating-code decode", () => {
  it("maps reading O-codes to as-recorded verdicts", () => {
    expect(decodeReadingVerdict(2)).toBe("pass");
    expect(decodeReadingVerdict(3)).toBe("fail");
    expect(decodeReadingVerdict(1)).toBeNull(); // no reading recorded
    expect(decodeReadingVerdict(0)).toBeNull(); // not applicable
    expect(decodeReadingVerdict(null)).toBeNull();
  });

  it("maps visual/pulsation status codes", () => {
    expect(decodeStatusVerdict(3)).toBe("fail");
    expect(decodeStatusVerdict(2)).toBe("pass");
    expect(decodeStatusVerdict(1)).toBeNull(); // blank / no fault
  });
});

describe("adaptLegacyReadings", () => {
  const payload = {
    legacy: { guid: "x", testNo: "1" },
    record1: {
      VLVacuumReceiverE: "45", VLVacuumReceiverO: 2, // working vacuum, pass
      VLNominalVacuumE: "45", // value only, no verdict
      VLVacuumRegulationE: "-0.2", VLVacuumRegulationO: 3, // deviation, fail
      RCEffectiveReserveE: "3342", RCEffectiveReserveO: 2, // pass
      RCRegulationLossE: "", RCRegulationLossO: 1, // no reading -> skipped
      RCFallVacuumDropE: "", RCFallVacuumDropO: 0, // not applicable -> skipped
    },
    record2: {
      VPCPumpCapacity1E: "4842", VPCPumpCapacity1O: 2, // pump 1 capacity, pass
      VPCPumpCapacity2E: "", VPCPumpCapacity2O: 1, // unused slot -> skipped
      VGAGaugeError1E: "0.3", VGAGaugeError1O: 2, // gauge error, pass
    },
    record3: {
      CAAClusterAirAdmissionE: "14", CAAClusterAirAdmissionO: 3, // over band, fail
      MMComment: "  Liners need replacing soon.  ",
    },
  };

  const out = adaptLegacyReadings(payload);

  it("maps values + as-recorded verdicts onto wizard reading keys", () => {
    expect(out.readings["tr.workingVacuum"]).toBe(45);
    expect(out.verdicts["tr.workingVacuum"]).toBe("pass");

    expect(out.readings["tr.regulationDeviation"]).toBe(-0.2);
    expect(out.verdicts["tr.regulationDeviation"]).toBe("fail");

    expect(out.readings["tr.effectiveReserve"]).toBe(3342);
    expect(out.verdicts["tr.effectiveReserve"]).toBe("pass");

    expect(out.readings["tr.pumpCapacity1"]).toBe(4842);
    expect(out.verdicts["tr.pumpCapacity1"]).toBe("pass");

    expect(out.readings["add.clusterAirAdmission"]).toBe(14);
    expect(out.verdicts["add.clusterAirAdmission"]).toBe("fail");
  });

  it("keeps value-only readings without a verdict", () => {
    expect(out.readings["tr.nominalVacuum"]).toBe(45);
    expect(out.verdicts["tr.nominalVacuum"]).toBeUndefined();
  });

  it("skips no-reading (O=1) and not-applicable (O=0) fields", () => {
    expect(out.readings["tr.regulationLoss"]).toBeUndefined();
    expect(out.verdicts["tr.regulationLoss"]).toBeUndefined();
    expect(out.readings["tr.fallOff"]).toBeUndefined();
    expect(out.readings["tr.pumpCapacity2"]).toBeUndefined();
  });

  it("extracts the tester comment, trimmed", () => {
    expect(out.comment).toBe("Liners need replacing soon.");
  });

  it("is safe on an empty/minimal payload", () => {
    const min = adaptLegacyReadings({ legacy: {}, signOff: {} });
    expect(Object.keys(min.readings)).toHaveLength(0);
    expect(Object.keys(min.verdicts)).toHaveLength(0);
    expect(min.comment).toBeUndefined();
  });
});
