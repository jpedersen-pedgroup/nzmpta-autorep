using System.Security.Claims;

namespace Autorep.Web.Domain;

/// <summary>
/// Who must use two-factor authentication, and how a signed-in account that still has to enrol
/// is marked. Enforcement is two-sided: Identity itself demands the code at sign-in once an
/// account has an authenticator, and <c>MfaEnrolmentMiddleware</c> keeps an account that
/// should have one but doesn't yet on the set-up page until it does.
/// </summary>
public static class MfaPolicy
{
    /// <summary>Claim stamped by <c>TesterClaimsPrincipalFactory</c> on a required account that has
    /// not enrolled. Its presence, not its absence, drives the redirect, so a principal from any
    /// other source (the JWT API, the test scheme) is unaffected unless it says so.</summary>
    public const string ClaimType = "autorep:mfa";
    public const string EnrolmentRequired = "enrolment-required";

    /// <summary>The roles that must have two-factor (PRD story 58: Super-Administrators). Widen
    /// here if NZMPTA extends it to Company Administrators.</summary>
    public static readonly string[] RequiredRoles = [Roles.SuperAdministrator];

    /// <summary>How long "trust this device" holds before the code is asked for again
    /// (PRD: required every 30 days and on any new or unrecognised device).</summary>
    public static readonly TimeSpan TrustedDeviceLifetime = TimeSpan.FromDays(30);

    public static bool IsRequiredFor(IEnumerable<string> roles) => roles.Any(RequiredRoles.Contains);

    public static bool MustEnrol(ClaimsPrincipal user) => user.HasClaim(ClaimType, EnrolmentRequired);

    /// <summary>What an unenrolled account may still reach: the account pages (set-up itself,
    /// recovery codes, settings, sign out, the gates), the error page and the health probe.</summary>
    public static bool IsAllowedWhileUnenrolled(PathString path) =>
        path.StartsWithSegments("/Account")
        || path.StartsWithSegments("/Error")
        || path.StartsWithSegments("/health");
}
