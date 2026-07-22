using System.Security.Claims;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Autorep.Web.Services;

/// <summary>
/// Stamps <see cref="LicenceScope.ScopeClaim"/> onto a lapsed Tester's principal.
///
/// Done here rather than in the login page so it holds on EVERY sign-in path. The licence check
/// in Login only runs on the password-success branch, so a Tester with two-factor enabled went
/// Login → TwoFactorChallenge → signed in, and never met it at all. Building the restriction into
/// the principal means one authorization policy covers every route in.
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

        return identity;
    }
}
