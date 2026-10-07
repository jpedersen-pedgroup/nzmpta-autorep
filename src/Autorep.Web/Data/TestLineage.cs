using Autorep.Web.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Data;

/// <summary>
/// The versions of one Machine Test. Every edit of a completed test is a new row naming the one it
/// replaces (<see cref="MachineTest.SupersedesClientId"/>) — and, for an automatic merge, the one it
/// combined (<see cref="MachineTest.MergedFromClientId"/>). All of them carry the original tester's
/// id and share <see cref="MachineTest.RootClientId"/>. Links are only ever followed within one
/// tester's rows: ClientId space is per tester.
/// </summary>
public static class TestLineage
{
    /// <summary>The key every version of a test shares. A row written before roots existed (or by a
    /// path that didn't set one) falls back to its own ClientId.</summary>
    public static Guid? KeyOf(MachineTest t) => t.RootClientId ?? t.ClientId;

    /// <summary>
    /// Every version of the test <paramref name="start"/> belongs to, itself included. Starts from
    /// the shared root and then follows the links both ways until nothing new turns up, so a chain
    /// with a row missing its root (written before the column existed, or seeded without one) is
    /// still found whole. Chains are a handful of versions, so this is a few small queries.
    /// </summary>
    public static async Task<List<MachineTest>> VersionsAsync(
        AutorepDbContext db, MachineTest start, CancellationToken ct)
    {
        var found = new Dictionary<Guid, MachineTest> { [start.Id] = start };
        if (KeyOf(start) is { } key)
        {
            foreach (var row in await db.MachineTests
                         .Where(t => t.TesterId == start.TesterId && t.RootClientId == key)
                         .ToListAsync(ct))
                found.TryAdd(row.Id, row);
        }

        var frontier = found.Values.ToList();
        while (frontier.Count > 0)
        {
            var ids = frontier.Where(t => t.ClientId != null).Select(t => t.ClientId).Distinct().ToList();
            var parents = frontier.SelectMany(t => new[] { t.SupersedesClientId, t.MergedFromClientId })
                .Where(g => g != null).Distinct().ToList();
            var linked = await db.MachineTests
                .Where(t => t.TesterId == start.TesterId
                    && (ids.Contains(t.SupersedesClientId)
                        || ids.Contains(t.MergedFromClientId)
                        || parents.Contains(t.ClientId)))
                .ToListAsync(ct);
            frontier = linked.Where(t => found.TryAdd(t.Id, t)).ToList();
        }
        return found.Values.ToList();
    }

    /// <summary>The versions that replace <paramref name="version"/>: the ones naming it as their
    /// predecessor or as the version they merged in.</summary>
    public static IEnumerable<MachineTest> SuccessorsOf(IEnumerable<MachineTest> versions, MachineTest version) =>
        version.ClientId is { } id
            ? versions.Where(v => v.Id != version.Id && (v.SupersedesClientId == id || v.MergedFromClientId == id))
            : [];

    /// <summary>
    /// The test's current version: completed, and not replaced by any completed version. An
    /// in-progress draft replaces nothing yet — it's combined with whatever is current when it's
    /// signed off. <paramref name="excludingClientId"/> leaves one version out of the reckoning (the
    /// one asking "what got there before me?"). Normally exactly one version qualifies; should a race
    /// ever leave two, the highest version wins, then the later completion — deterministic either way.
    /// </summary>
    public static MachineTest? Head(IEnumerable<MachineTest> versions, Guid? excludingClientId = null)
    {
        var candidates = versions
            .Where(v => excludingClientId is null || v.ClientId != excludingClientId)
            .ToList();
        var replaced = candidates
            .Where(v => v.MarkedCompleteAt != null)
            .SelectMany(v => new[] { v.SupersedesClientId, v.MergedFromClientId })
            .Where(g => g != null)
            .Select(g => g!.Value)
            .ToHashSet();
        return candidates
            .Where(v => v.MarkedCompleteAt != null && (v.ClientId is not { } id || !replaced.Contains(id)))
            .OrderByDescending(v => v.Version)
            .ThenByDescending(v => v.MarkedCompleteAt)
            .ThenByDescending(v => v.CreatedAt)
            .FirstOrDefault();
    }

    /// <summary>
    /// The root a new version takes: its parent's, when the parent is on the server; the parent's own
    /// ClientId when it isn't yet (a device pushes in whatever order its store lists tests, so a v2
    /// can arrive before its v1); its own ClientId for an original.
    /// </summary>
    public static async Task<Guid> RootForNewVersionAsync(
        AutorepDbContext db, string testerId, Guid clientId, Guid? parentClientId, CancellationToken ct)
    {
        if (parentClientId is not { } parent) return clientId;
        var parentRoot = await db.MachineTests
            .Where(t => t.TesterId == testerId && t.ClientId == parent)
            .Select(t => new { Root = t.RootClientId ?? t.ClientId })
            .FirstOrDefaultAsync(ct);
        return parentRoot?.Root ?? parent;
    }

    /// <summary>A version that reaches the server after versions made from it adopts them: anything
    /// provisionally rooted at its ClientId takes its root instead (a no-op for an original, whose
    /// root is its own ClientId already).</summary>
    public static async Task AdoptDescendantsAsync(
        AutorepDbContext db, string testerId, Guid clientId, Guid root, CancellationToken ct)
    {
        if (root == clientId) return;
        var provisional = await db.MachineTests
            .Where(t => t.TesterId == testerId && t.RootClientId == clientId && t.ClientId != clientId)
            .ToListAsync(ct);
        foreach (var row in provisional) row.RootClientId = root;
    }
}
