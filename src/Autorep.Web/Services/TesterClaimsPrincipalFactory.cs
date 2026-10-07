using System.Security.Claims;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Autorep.Web.Services;

/// <summary>
/// Stamps the claims that scope a session onto the principal: <see cref="LicenceScope.ScopeClaim"/>
/// for a lapsed Tester, and <see cref="MfaPolicy.ClaimType"/> for an account that must use
/// two-factor but hasn't set it up.
///
/// Done here rather than in the login page so it holds on EVERY sign-in path. The licence check
/// in Login only ran on the password-success branch, so a Tester with two-factor enabled went
/// Login → TwoFactorChallenge → signed in, and never met it at all. Building the restriction into
/// the principal means one authorization policy (or one middleware) covers every route in, and
/// the security-stamp validator refreshes it on its own schedule.
/// </summary>
public class TesterClaimsPrincipalFactory : UserClaimsPrincipalFactory<Tester, IdentityRole>
{
    public TesterClaimsPrincipalFactory(
        UserManager<Tester> userManager,
        RoleManager<IdentityRole> roleManager,
        IOptions<IdentityOptions> options)
        : base(userManager, roleManager, options)
    {
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(Tester user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        var roles = await UserManager.GetRolesAsync(user);
        if (LicenceScope.IsSyncOnly(user.LicenceExpiryDate, roles, DateOnly.FromDateTime(DateTime.UtcNow)))
        {
            identity.AddClaim(new Claim(LicenceScope.ScopeClaim, LicenceScope.SyncOnly));
        }

        // Read straight off the entity, not GetTwoFactorEnabledAsync: the store's answer is this
        // column, and the factory already holds the row.
        if (MfaPolicy.IsRequiredFor(roles) && !user.TwoFactorEnabled)
        {
            identity.AddClaim(new Claim(MfaPolicy.ClaimType, MfaPolicy.EnrolmentRequired));
        }

        return identity;
    }
}
