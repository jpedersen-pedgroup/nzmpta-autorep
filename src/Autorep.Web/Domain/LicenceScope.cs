namespace Autorep.Web.Domain;

/// <summary>
/// What a signed-in Tester may do once their licence has lapsed.
///
/// An expired licence must not lock a Tester out entirely: a Machine Test can only be pushed by
/// the Tester it belongs to (the sync surface attributes every upload to the caller), so refusing
/// them a session would strand any capture still queued on their device with no way for anyone
/// else to send it. They keep a session, marked sync-only — every Tester surface is closed to it
/// and the only thing it can do is flush that queue.
/// </summary>
public static class LicenceScope
{
    /// <summary>Claim type carrying the restriction. Absent on an unrestricted session.</summary>
    public const string ScopeClaim = "autorep:scope";

    /// <summary>Claim value for "may sync already-captured work, and nothing else".</summary>
    public const string SyncOnly = "sync-only";

    /// <summary>
    /// True for a Tester whose licence has lapsed. Multi-role users are unaffected — an
    /// administrator's access does not hang off a testing licence.
    /// </summary>
    public static bool IsSyncOnly(DateOnly? licenceExpiry, ICollection<string> roles, DateOnly today)
        => licenceExpiry is { } expiry
            && expiry < today
            && roles.Contains(Roles.Tester)
            && !roles.Contains(Roles.SuperAdministrator)
            && !roles.Contains(Roles.CompanyAdministrator);
}
