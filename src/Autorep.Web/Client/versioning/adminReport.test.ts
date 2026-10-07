import { describe, expect, it, vi } from "vitest";
import type { AmendmentRecord, LocalTest } from "../db/testStore";
import { defaultMachineConfiguration } from "../wizard/types";
import type { ReportPdf } from "../report/testSummaryPdf";
import { storeAdminVersionReport, type AdminReportDeps } from "./adminReport";
import { savedByAdministrator } from "./chain";

const record = (over: Partial<AmendmentRecord> = {}): AmendmentRecord => ({
  version: 2,
  amendedAt: "2026-10-08T01:00:00.000Z",
  baseVersion: 1,
  changes: [],
  ...over,
});

function adminVersion(over: Partial<LocalTest> = {}): LocalTest {
  return {
    id: "server-id-2",
    farmName: "Kowhai Flats",
    config: defaultMachineConfiguration(),
    currentStep: "ReviewSignOff",
    visualFaults: {},
    attestations: [],
    readings: {},
    recommendations: {},
    dataFields: {},
    createdAt: "2026-09-01T00:00:00.000Z",
    updatedAt: "2026-10-08T01:00:00.000Z",
    markedCompleteAt: "2026-09-01T01:00:00.000Z",
    syncState: "uploaded",
    version: 2,
    amendments: [record({ amendedByName: "Sam Superadmin", amendedByRole: "Super Administrator", reason: "Corrected" })],
    ...over,
  };
}

const pdf = (over: Partial<ReportPdf> = {}): ReportPdf => ({
  bytes: new TextEncoder().encode("%PDF-1.7 report"),
  fileName: "Test Summary.pdf",
  analyser: "none",
  ...over,
});

function deps(made: ReportPdf | Error, answer?: Response | Error): AdminReportDeps & { put: ReturnType<typeof vi.fn> } {
  return {
    make: vi.fn(async () => {
      if (made instanceof Error) throw made;
      return made;
    }),
    put: vi.fn(async () => {
      if (answer instanceof Error) throw answer;
      return answer ?? new Response(null, { status: 201 });
    }),
  };
}

describe("storeAdminVersionReport", () => {
  it("sends the version's report to the admin route under the server's id", async () => {
    const d = deps(pdf());
    const outcome = await storeAdminVersionReport(adminVersion(), undefined, null, d);
    expect(outcome).toEqual({ kind: "stored", sizeBytes: pdf().bytes.length });
    expect(d.put).toHaveBeenCalledWith("server-id-2", pdf().bytes);
  });

  it("counts an unchanged copy (a retry) as kept", async () => {
    const outcome = await storeAdminVersionReport(adminVersion(), undefined, null, deps(pdf(), new Response(null, { status: 200 })));
    expect(outcome.kind).toBe("stored");
  });

  it.each(["unreachable", "merger-unavailable"] as const)(
    "doesn't keep a copy missing the analyser's pages (%s)",
    async (analyser) => {
      const d = deps(pdf({ analyser }));
      const outcome = await storeAdminVersionReport(adminVersion(), undefined, null, d);
      expect(outcome.kind).toBe("failed");
      expect(d.put).not.toHaveBeenCalled();
    },
  );

  it("keeps one whose analyser PDF can never be read, as the tester's device would", async () => {
    const d = deps(pdf({ analyser: "unreadable" }));
    expect((await storeAdminVersionReport(adminVersion(), undefined, null, d)).kind).toBe("stored");
  });

  it("says when the session has ended", async () => {
    const outcome = await storeAdminVersionReport(adminVersion(), undefined, null, deps(pdf(), new Response(null, { status: 401 })));
    expect(outcome).toEqual({ kind: "signed-out" });
  });

  it("passes on the server's reason, or says it couldn't reach it", async () => {
    const refused = new Response(JSON.stringify({ message: "The report store is unavailable right now." }), { status: 503 });
    expect(await storeAdminVersionReport(adminVersion(), undefined, null, deps(pdf(), refused))).toEqual({
      kind: "failed",
      message: "The report store is unavailable right now.",
    });
    expect((await storeAdminVersionReport(adminVersion(), undefined, null, deps(pdf(), new TypeError("offline")))).kind).toBe("failed");
    expect((await storeAdminVersionReport(adminVersion(), undefined, null, deps(new Error("no generator")))).kind).toBe("failed");
  });
});

describe("savedByAdministrator", () => {
  it("is an administrator's version, not a tester's amendment or an automatic merge", () => {
    expect(savedByAdministrator(adminVersion())).toBe(true);
    expect(savedByAdministrator(adminVersion({ amendments: [record()] }))).toBe(false);
    expect(
      savedByAdministrator(
        adminVersion({
          amendments: [
            record({
              amendedByRole: "Automatic merge",
              merge: { headVersion: 2, fromVersion: 2, fromId: "x", overlaps: [] },
            }),
          ],
        }),
      ),
    ).toBe(false);
    // A record carried from an earlier version isn't this version's own.
    expect(savedByAdministrator(adminVersion({ version: 3 }))).toBe(false);
  });
});
