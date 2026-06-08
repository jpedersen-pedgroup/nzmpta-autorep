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
    public DateTimeOffset? MarkedCompleteAt { get; set; }
    public string? Notes { get; set; }

    /// <summary>The upfront Machine Configuration that drives the wizard steps and the standards
    /// used for pass/fail (1:1; created alongside the test in Phase 2+).</summary>
    public MachineConfiguration? Configuration { get; set; }

    // Client-generated id used for upsert-by-client during sync, so the
    // same test created offline on a Device doesn't duplicate on retry.
    public Guid? ClientId { get; set; }
}
