namespace Autorep.Web.Domain.Entities;

/// <summary>
/// Two versions of one test that both replaced the same earlier version — the collision every
/// edit-as-a-new-version model has instead of an overwrite. In practice a tester edits a completed
/// test offline while an administrator edits it online: whichever reaches the server first is the
/// <see cref="HeadClientId"/>, the other the <see cref="IncomingClientId"/>. The tester's device
/// combines them field by field (Client/versioning/merge.ts) into <see cref="MergedClientId"/>;
/// every version stays on record. Kept for the 7-year audit window like the versions themselves.
/// </summary>
public class SyncConflict
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Whose test: every version in the chain carries the original tester's id.</summary>
    public string TesterId { get; set; } = string.Empty;

    /// <summary>The test (see <see cref="MachineTest.RootClientId"/>).</summary>
    public Guid? RootClientId { get; set; }

    /// <summary>The version both sides started from.</summary>
    public Guid BaseClientId { get; set; }

    /// <summary>The version already on the server when the collision was found.</summary>
    public Guid HeadClientId { get; set; }

    /// <summary>The version that arrived second.</summary>
    public Guid IncomingClientId { get; set; }

    /// <summary>The combined version, once the merge has been stored.</summary>
    public Guid? MergedClientId { get; set; }

    /// <summary>One of <see cref="SyncConflictStatus"/>.</summary>
    public string Status { get; set; } = SyncConflictStatus.Pending;

    /// <summary>Where it was found: a tester's push, or an administrator's save.</summary>
    public string DetectedOn { get; set; } = SyncConflictSource.Push;

    public DateTimeOffset DetectedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ResolvedAt { get; set; }

    /// <summary>The fields both versions changed, as payload paths (JSON array), computed by the
    /// server from the three stored versions when the merge lands. The combined version's own
    /// amendment record names them in words and says which value was kept.</summary>
    public string? OverlappingFieldsJson { get; set; }
}

public static class SyncConflictStatus
{
    /// <summary>The incoming version is still in progress; it is combined when it's signed off.</summary>
    public const string Pending = "Pending";

    /// <summary>Combined into <see cref="SyncConflict.MergedClientId"/>.</summary>
    public const string Merged = "Merged";
}

public static class SyncConflictSource
{
    public const string Push = "Push";
    public const string Save = "Save";
}
