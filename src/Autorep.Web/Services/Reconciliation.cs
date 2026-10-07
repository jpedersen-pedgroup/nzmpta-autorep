using System.Text.Json;
using Autorep.Web.Data;
using Autorep.Web.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Services;

/// <summary>
/// The server's half of sync reconciliation (PRD §Decisions). Every edit of a completed test is a new
/// version, so nothing is ever overwritten; what can happen is two versions replacing the same one —
/// typically a tester's offline edit and an administrator's online edit of the same completed test.
/// The server spots that when the second of them arrives, keeps both, records a
/// <see cref="SyncConflict"/>, and hands the tester's device what it needs to combine them: the
/// version both started from and the one already on the server. The device does the field-by-field
/// combine (Client/versioning/merge.ts) because the calculated readings and the human-readable
/// amendment record come from the same TypeScript the wizard uses; the server checks the result
/// lines up with the chain and stores all three.
/// </summary>
public sealed class Reconciliation(AutorepDbContext db)
{
    /// <summary>A version arriving after another version already replaced its parent.</summary>
    /// <param name="Base">The parent both replaced.</param>
    /// <param name="Head">The test's current version, which the arrival didn't know about.</param>
    public sealed record Collision(MachineTest Base, MachineTest Head);

    /// <summary>
    /// What a version (<paramref name="clientId"/>, replacing <paramref name="parentClientId"/>)
    /// collides with, or null when nothing else replaced its parent first. Only a completed rival
    /// counts — an in-progress draft replaces nothing yet. A version already combined into a merge
    /// collides with nothing: re-sending it changes nothing about the chain.
    /// </summary>
    public async Task<Collision?> CollisionAsync(
        string testerId, Guid clientId, Guid parentClientId, CancellationToken ct)
    {
        if (await db.MachineTests.AnyAsync(t => t.TesterId == testerId && t.MergedFromClientId == clientId, ct))
            return null;
        var parent = await db.MachineTests
            .FirstOrDefaultAsync(t => t.TesterId == testerId && t.ClientId == parentClientId, ct);
        // Its parent hasn't reached the server, so nothing on the server can have replaced it.
        if (parent is null) return null;

        var versions = await TestLineage.VersionsAsync(db, parent, ct);
        var rivalled = TestLineage.SuccessorsOf(versions, parent)
            .Any(v => v.ClientId != clientId && v.MarkedCompleteAt != null);
        if (!rivalled) return null;
        var head = TestLineage.Head(versions, excludingClientId: clientId);
        return head is null ? null : new Collision(parent, head);
    }

    /// <summary>
    /// Notes that <paramref name="incomingClientId"/> and <paramref name="head"/> both replace
    /// <paramref name="baseVersion"/> and haven't been combined yet. One pending record per incoming
    /// version: a later sighting only moves its head on (the test may have been edited again since).
    /// </summary>
    public async Task NotePendingAsync(
        MachineTest baseVersion, MachineTest head, Guid incomingClientId, string detectedOn, CancellationToken ct)
    {
        var pending = await db.SyncConflicts.FirstOrDefaultAsync(c =>
            c.TesterId == baseVersion.TesterId
            && c.IncomingClientId == incomingClientId
            && c.Status == SyncConflictStatus.Pending, ct);
        if (pending is not null)
        {
            if (pending.HeadClientId != head.ClientId) pending.HeadClientId = head.ClientId!.Value;
            return;
        }
        db.SyncConflicts.Add(new SyncConflict
        {
            TesterId = baseVersion.TesterId,
            RootClientId = TestLineage.KeyOf(baseVersion),
            BaseClientId = baseVersion.ClientId!.Value,
            HeadClientId = head.ClientId!.Value,
            IncomingClientId = incomingClientId,
            DetectedOn = detectedOn,
        });
    }

    /// <summary>
    /// Records the combine: the pending record for <paramref name="incoming"/> (or a new one, if it
    /// was never seen pending) becomes Merged, naming the head it was combined with, the result, and
    /// the fields both sides changed — worked out here from the three stored payloads, not taken
    /// from the device.
    /// </summary>
    public async Task<SyncConflict> NoteMergedAsync(
        MachineTest baseVersion, MachineTest head, MachineTest incoming, MachineTest merged,
        string? incomingPayload, CancellationToken ct)
    {
        var record = await db.SyncConflicts.FirstOrDefaultAsync(c =>
            c.TesterId == baseVersion.TesterId
            && c.IncomingClientId == incoming.ClientId
            && c.Status == SyncConflictStatus.Pending, ct);
        if (record is null)
        {
            record = new SyncConflict
            {
                TesterId = baseVersion.TesterId,
                RootClientId = TestLineage.KeyOf(baseVersion),
                BaseClientId = baseVersion.ClientId!.Value,
                IncomingClientId = incoming.ClientId!.Value,
                DetectedOn = SyncConflictSource.Push,
            };
            db.SyncConflicts.Add(record);
        }
        record.HeadClientId = head.ClientId!.Value;
        record.MergedClientId = merged.ClientId;
        record.Status = SyncConflictStatus.Merged;
        record.ResolvedAt = DateTimeOffset.UtcNow;
        record.OverlappingFieldsJson = JsonSerializer.Serialize(PayloadUnits.Overlapping(
            PayloadUnits.Parse(baseVersion.PayloadJson),
            PayloadUnits.Parse(head.PayloadJson),
            PayloadUnits.Parse(incomingPayload)));
        return record;
    }
}
