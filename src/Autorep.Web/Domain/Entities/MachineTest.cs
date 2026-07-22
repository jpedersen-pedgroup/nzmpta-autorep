namespace Autorep.Web.Domain.Entities;

// Phase 1 walking-skeleton minimum (Farm + Tester + completion timestamp).
// Phases 2-4 grow this with the full configuration, numerical readings,
// faults, recommendations and Test Versions.
public class MachineTest
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string TesterId { get; set; } = string.Empty;
    public Tester? Tester { get; set; }

    public Guid FarmId { get; set; }
    public Farm? Farm { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Server-side watermark for delta sync: stamped (server clock) whenever this row is
    /// written through the sync surface. Deliberately NOT the device's updatedAt — device clocks
    /// can't be trusted to order pulls.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? MarkedCompleteAt { get; set; }
    public string? Notes { get; set; }

    /// <summary>The upfront Machine Configuration that drives the wizard steps and the standards
    /// used for pass/fail (1:1; created alongside the test in Phase 2+).</summary>
    public MachineConfiguration? Configuration { get; set; }

    // Client-generated id used for upsert-by-client during sync, so the
    // same test created offline on a Device doesn't duplicate on retry.
    public Guid? ClientId { get; set; }

    /// <summary>The Testing Company this work was done for, stamped once when the test is first
    /// uploaded and never re-stamped. Deliberately NOT derived from the owner's CURRENT company
    /// (the way Farm scoping is): a tester who moves companies would otherwise expose every test
    /// they did for their previous employer to their new colleagues. Null means the tester had no
    /// company at upload time — such a row is simply invisible to the company-scoped surfaces.</summary>
    public Guid? TestingCompanyId { get; set; }

    /// <summary>1 for an original test; incremented each time a completed test is reopened as a new
    /// version. Also carried inside PayloadJson — mirrored here so the server can order and filter
    /// versions without parsing (and materialising) the payload.</summary>
    public int Version { get; set; } = 1;

    /// <summary>The ClientId of the version this one replaces, or null for an original. Only ever
    /// resolved against the SAME tester's rows (ClientId space is per-tester — see the unique index
    /// in AutorepDbContext), so a push can't claim to supersede another tester's test.</summary>
    public Guid? SupersedesClientId { get; set; }

    /// <summary>The full offline capture payload (visual faults, readings, recommendations,
    /// data-fields, per-pulsator/cluster rows, attestations, calibration dates) serialised as JSON.
    /// The Device is the source of truth; this round-trips it for sync + reprint. Queryable header
    /// fields stay in their own columns; the rich detail lives here.</summary>
    public string? PayloadJson { get; set; }
}
