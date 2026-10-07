using System.Globalization;
using System.Security.Claims;

namespace Autorep.Web.Domain;

/// <summary>
/// Who must use two-factor authentication, how a signed-in account that still has to enrol is
/// marked, and how long a session may run on one proof of the second factor. Enforcement is
/// three-sided: Identity itself demands the code at sign-in once an account has an authenticator;
/// <c>MfaEnrolmentMiddleware</c> keeps an account that should have one but doesn't yet on the
/// set-up page until it does; and the same middleware ends a required-role session whose last
/// proof is older than <see cref="TrustedDeviceLifetime"/>, however busy it has been.
/// </summary>
public static class MfaPolicy
{
    /// <summary>Claim stamped by <c>TesterClaimsPrincipalFactory</c> on a required account that has
    /// not enrolled. Its presence, not its absence, drives the redirect, so a principal from any
    /// other source (the JWT API, the test scheme) is unaffected unless it says so.</summary>
    public const string ClaimType = "autorep:mfa";
    public const string EnrolmentRequired = "enrolment-required";

    /// <summary>Authentication-properties item, set by <c>TesterSignInManager</c>: when this session
    /// last proved its second factor (round-trip "o" format). Properties live in the cookie
    /// ticket and survive both sliding renewal and the stamp validator's principal refresh.</summary>
    public const string SessionStampKey = "autorep:mfa_at";

    /// <summary>The roles that must have two-factor (PRD story 58: Super-Administrators). Widen
    /// here if NZMPTA extends it to Company Administrators.</summary>
    public static readonly string[] RequiredRoles = [Roles.SuperAdministrator];

    /// <summary>How long one proof of the second factor holds - for "trust this device" and for a
    /// live session alike (PRD: required every 30 days and on any new or unrecognised device).</summary>
    public static readonly TimeSpan TrustedDeviceLifetime = TimeSpan.FromDays(30);

    public static bool IsRequiredFor(IEnumerable<string> roles) => roles.Any(RequiredRoles.Contains);

    public static bool IsRequiredFor(ClaimsPrincipal user) => RequiredRoles.Any(user.IsInRole);

    public static bool MustEnrol(ClaimsPrincipal user) => user.HasClaim(ClaimType, EnrolmentRequired);

    /// <summary>True when the session's recorded proof is older than the lifetime. A session with
    /// no stamp (a password-only sign-in, or one issued before stamps existed) has no deadline
    /// here: it is bounded by the application cookie and challenged at its next sign-in.</summary>
    public static bool SessionExpired(IDictionary<string, string?> items, DateTimeOffset now)
    {
        if (!items.TryGetValue(SessionStampKey, out var raw) || string.IsNullOrEmpty(raw)) return false;
        if (!DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at))
            return true; // unreadable is treated as stale, never as fresh
        return now - at > TrustedDeviceLifetime;
    }

    /// <summary>What an unenrolled account may still reach: the account pages (set-up itself,
    /// recovery codes, settings, sign out, the gates), the error page and the health probe.</summary>
    public static bool IsAllowedWhileUnenrolled(PathString path) =>
        path.StartsWithSegments("/Account")
        || path.StartsWithSegments("/Error")
        || path.StartsWithSegments("/health");
}
