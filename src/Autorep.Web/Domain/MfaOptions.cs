using System.Security.Claims;

namespace Autorep.Web.Domain;

/// <summary>
/// The roles that must use two-factor, bound from <c>Mfa:RequiredRoles</c>. PRD story 58 asks it of
/// Super-Administrators and PR #71 enforced that; on 3 Nov 2026 MPNZ asked for it to be off for
/// administrators for now, so the default is no role at all. Two-factor itself stays: anyone can
/// still set it up from their account page, and an account with an authenticator is still asked
/// for its code. This decides only who is made to set it up, who can't switch it off, and whose
/// session is capped at <see cref="MfaPolicy.TrustedDeviceLifetime"/> from its last code.
///
/// To turn it back on, list the roles - <c>"RequiredRoles": ["SuperAdministrator"]</c> in
/// appsettings.json (an app setting set by hand in Azure is lost on the next infra deploy), and
/// add <c>CompanyAdministrator</c> to widen it. A name that isn't a role stops the app at start-up
/// rather than quietly requiring nothing.
/// </summary>
public class MfaOptions
{
    public const string Section = "Mfa";

    public string[] RequiredRoles { get; set; } = [];

    public bool IsRequiredFor(IEnumerable<string> roles) => roles.Any(RequiredRoles.Contains);

    public bool IsRequiredFor(ClaimsPrincipal user) => RequiredRoles.Any(user.IsInRole);
}
