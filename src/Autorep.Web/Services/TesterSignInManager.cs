using System.Security.Claims;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Autorep.Web.Services;

/// <summary>
/// Identity's sign-in manager, plus one thing: every application cookie it issues records WHEN
/// the account last proved its second factor, in the ticket's authentication properties
/// (<see cref="MfaPolicy.SessionStampKey"/>). Properties survive the cookie's sliding renewal and
/// the security-stamp validator's principal refresh, which claims do not, so the stamp is a fixed
/// point the middleware can hold a live session to: an administrator who never signs out is still
/// re-challenged after <see cref="MfaPolicy.TrustedDeviceLifetime"/>, as the PRD asks.
/// </summary>
public class TesterSignInManager : SignInManager<Tester>
{
    public TesterSignInManager(
        UserManager<Tester> userManager,
        IHttpContextAccessor contextAccessor,
        IUserClaimsPrincipalFactory<Tester> claimsFactory,
        IOptions<IdentityOptions> optionsAccessor,
        ILogger<SignInManager<Tester>> logger,
        IAuthenticationSchemeProvider schemes,
        IUserConfirmation<Tester> confirmation)
        : base(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
    {
    }

    public override async Task SignInWithClaimsAsync(
        Tester user, AuthenticationProperties? authenticationProperties, IEnumerable<Claim> additionalClaims)
    {
        var claims = additionalClaims as IList<Claim> ?? additionalClaims.ToList();
        var props = authenticationProperties ?? new AuthenticationProperties();

        // RefreshSignInAsync passes the existing properties through: an existing stamp stays.
        if (!props.Items.ContainsKey(MfaPolicy.SessionStampKey))
        {
            DateTimeOffset? rememberedAt = null;
            if (user.TwoFactorEnabled)
            {
                // A trusted device signs in on the password alone; its second factor was proved
                // when the (non-sliding) trust cookie was issued.
                // Its expiry is fixed at issue + lifetime (no sliding) and survives the stamp
                // validator's renewals, which reset IssuedUtc; so the issue time is derived from it.
                // Identity's StoreRememberClient puts the user id in the Name claim (the same
                // comparison IsTwoFactorClientRememberedAsync makes), not NameIdentifier.
                var remembered = await Context.AuthenticateAsync(IdentityConstants.TwoFactorRememberMeScheme);
                if (remembered.Succeeded
                    && remembered.Principal?.FindFirstValue(ClaimTypes.Name) == user.Id)
                {
                    rememberedAt = remembered.Properties?.ExpiresUtc is { } expires
                        ? expires - MfaPolicy.TrustedDeviceLifetime
                        : remembered.Properties?.IssuedUtc;
                }
            }

            var stamp = MfaStampFor(
                mfaClaim: claims.Any(c => c.Type == "amr" && c.Value == "mfa"),
                rememberedAt,
                DateTimeOffset.UtcNow);
            if (stamp is { } at) props.Items[MfaPolicy.SessionStampKey] = at.ToString("o");
        }

        await base.SignInWithClaimsAsync(user, props, claims);
    }

    /// <summary>Re-issues the current session as one that has just proved its second factor - for
    /// the set-up page, where the code the user has just verified is as good as a challenge.
    /// Like <see cref="SignInManager{TUser}.RefreshSignInAsync"/> it keeps the ticket's properties
    /// (persistence and all), but writes a fresh stamp and marks the principal <c>amr=mfa</c>.</summary>
    public async Task RefreshSignInAfterSecondFactorAsync(Tester user)
    {
        var auth = await Context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var props = auth.Properties ?? new AuthenticationProperties();
        props.Items[MfaPolicy.SessionStampKey] = DateTimeOffset.UtcNow.ToString("o");
        await base.SignInWithClaimsAsync(user, props, [new Claim("amr", "mfa")]);
    }

    /// <summary>When this session last proved its second factor: now, if the sign-in itself carried
    /// the code (Identity marks that with <c>amr=mfa</c>); the trust cookie's issue time for a
    /// remembered device; nothing for a password-only sign-in, which has no deadline to hold.</summary>
    public static DateTimeOffset? MfaStampFor(bool mfaClaim, DateTimeOffset? rememberedAt, DateTimeOffset now)
        => mfaClaim ? now : rememberedAt;
}
