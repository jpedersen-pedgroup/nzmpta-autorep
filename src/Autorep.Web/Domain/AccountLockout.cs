namespace Autorep.Web.Domain;

/// <summary>
/// Admin "deactivate" parks an account's lockout a century into the future — far beyond any
/// transient failed-attempts lockout (which lasts minutes). The setter and the detector live
/// together here so the two thresholds can never drift apart.
/// </summary>
public static class AccountLockout
{
    private const int DeactivatedYears = 100;

    /// <summary>Lockout-end value used to deactivate an account.</summary>
    public static DateTimeOffset DeactivatedUntil() => DateTimeOffset.UtcNow.AddYears(DeactivatedYears);

    /// <summary>
    /// True if the lockout is the admin "deactivated" sentinel rather than a short
    /// failed-attempts lockout. The half-life threshold sits comfortably above any transient
    /// lockout yet below the full sentinel, so it stays correct as the clock advances.
    /// </summary>
    public static bool IsDeactivated(DateTimeOffset? lockoutEnd) =>
        lockoutEnd is { } end && end > DateTimeOffset.UtcNow.AddYears(DeactivatedYears / 2);
}
