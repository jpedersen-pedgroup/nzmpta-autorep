using System.Security.Claims;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Autorep.Web.Services;

/// <summary>
/// Stamps <see cref="MfaPolicy.ClaimType"/> onto the principal of an account that must use
/// two-factor but hasn't set it up.
///
/// Done here rather than in the login page so it holds on EVERY sign-in path: a check in Login
/// would only run on the password-success branch, and an account with two-factor enabled goes
/// Login → TwoFactorChallenge → signed in without passing it. Building the restriction into the
/// principal means one middleware covers every route in, and the security-stamp validator
/// refreshes it on its own schedule.
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

        // Read straight off the entity, not GetTwoFactorEnabledAsync: the store's answer is this
        // column, and the factory already holds the row.
        var roles = await UserManager.GetRolesAsync(user);
        if (MfaPolicy.IsRequiredFor(roles) && !user.TwoFactorEnabled)
        {
            identity.AddClaim(new Claim(MfaPolicy.ClaimType, MfaPolicy.EnrolmentRequired));
        }

        return identity;
    }
}
