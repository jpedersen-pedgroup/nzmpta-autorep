using System.Text.Encodings.Web;
using System.Text.Json;
using Autorep.Web.Data;
using Autorep.Web.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Services;

/// <summary>
/// A Super-Administrator's soft-delete of a test, with a mandatory reason (PRD story 70; PRD
/// §Decisions: "no one hard-deletes"). The TEST is deleted — every version of it — so it leaves every
/// list, the Upcoming page and the tester's device, while the rows stay on record with who deleted
/// them, when and why. Each version's UpdatedAt moves, so a device's next delta pull receives it as a
/// tombstone. Restoring brings every version back the same way.
///
/// A deletion and a sync can land at the same moment. Both sides write the versions' concurrency
/// stamp (<see cref="MachineTest.SuccessorStamp"/>) — a push renews its parent's when it starts or
/// signs off a version, and a deletion renews every version's — so whichever saves second fails and
/// retries, rather than a new version slipping past the deletion: the administrator's retry includes
/// it, and the device's retry is stored as deleted.
/// </summary>
public sealed class TestDeletion(AutorepDbContext db)
{
    public const int MaxReasonLength = 500;

    public abstract record Result;
    public sealed record Done(IReadOnlyList<MachineTest> Versions) : Result;
    public sealed record Missing : Result;
    public sealed record Invalid(string Message) : Result;
    /// <summary>Already deleted (or, for a restore, not deleted) — or changed in the same moment.</summary>
    public sealed record Conflict(string Error) : Result;

    private static readonly JsonSerializerOptions Write = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public async Task<Result> DeleteAsync(Guid id, string? reason, string actorId, CancellationToken ct)
    {
        var why = reason?.Trim();
        if (string.IsNullOrEmpty(why)) return new Invalid("Give a reason — it's kept with the deleted test.");
        if (why.Length > MaxReasonLength) return new Invalid($"Keep the reason under {MaxReasonLength} characters.");

        var test = await db.MachineTests.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (test is null) return new Missing();
        if (test.IsDeleted) return new Conflict("deleted");

        var versions = await TestLineage.VersionsAsync(db, test, ct);
        var now = DateTimeOffset.UtcNow;
        foreach (var version in versions)
        {
            version.IsDeleted = true;
            version.DeletedAt = now;
            version.DeletedById = actorId;
            version.DeletedReason = why;
            version.UpdatedAt = now;
            version.SuccessorStamp = Guid.NewGuid();
        }
        Audit(actorId, test, "SoftDeleted", new { reason = why, versions = Summaries(versions) });
        return await SaveAsync(versions, ct);
    }

    public async Task<Result> RestoreAsync(Guid id, string actorId, CancellationToken ct)
    {
        var test = await db.MachineTests.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (test is null) return new Missing();
        if (!test.IsDeleted) return new Conflict("not-deleted");

        var versions = await TestLineage.VersionsAsync(db, test, ct);
        var now = DateTimeOffset.UtcNow;
        var previous = new { reason = test.DeletedReason, deletedAt = test.DeletedAt, deletedById = test.DeletedById };
        foreach (var version in versions.Where(v => v.IsDeleted))
        {
            version.IsDeleted = false;
            version.DeletedAt = null;
            version.DeletedById = null;
            version.DeletedReason = null;
            version.UpdatedAt = now;
            version.SuccessorStamp = Guid.NewGuid();
        }
        Audit(actorId, test, "Restored", new { previous, versions = Summaries(versions) });
        return await SaveAsync(versions, ct);
    }

    private static object Summaries(IEnumerable<MachineTest> versions) =>
        versions.OrderBy(v => v.Version).Select(v => new { v.Id, v.ClientId, v.Version }).ToList();

    private void Audit(string actorId, MachineTest test, string operation, object detail) =>
        db.AuditEntries.Add(new AuditEntry
        {
            Actor = actorId,
            EntityType = nameof(MachineTest),
            EntityKey = test.Id.ToString(),
            Operation = operation,
            AfterJson = JsonSerializer.Serialize(detail, Write),
        });

    private async Task<Result> SaveAsync(IReadOnlyList<MachineTest> versions, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return new Done(versions);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A version of this test was written in the same moment (a sync, an edit): nothing was
            // changed; the administrator retries.
            return new Conflict("busy");
        }
    }
}
