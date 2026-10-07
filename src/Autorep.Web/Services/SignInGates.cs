using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Services;

/// <summary>
/// The checks a correct password alone doesn't settle, applied once the account is actually
/// signed in: straight after the password for an account without two-factor, after the code or
/// recovery code for one with it. Before this lived in the login page's password-success branch
/// only, so a two-factor account went Login → challenge → signed in and met none of them: a
/// migrated Super-Administrator with two-factor was never forced to change the legacy password.
/// </summary>
public class SignInGates(
    SignInManager<Tester> signIn,
    UserManager<Tester> users,
    AutorepDbContext db,
    LoginAudit audit)
{
    public abstract record Gate;

    /// <summary>Temporary or migrated password: signed out again, sent to set a real one.</summary>
    public sealed record ResetPassword(string Email, string Token) : Gate;

    /// <summary>Lapsed Tester licence: the session stands but is sync-only (see
    /// <see cref="LicenceScope"/>), so the flush page is all that's left to offer.</summary>
    public sealed record SyncOnly : Gate;

    /// <summary>Terms of use changed, or the licence was renewed since they were accepted.</summary>
    public sealed record AcceptTerms(string Email, string Token) : Gate;

    /// <summary>In order of precedence. The reset gate comes first so a temporary password can
    /// never be parlayed into a sync-only session; the licence check runs only once the password
    /// (and code) are known good, so it can't be used to enumerate accounts.</summary>
    public async Task<Gate?> CheckAsync(HttpContext ctx, Tester user)
    {
        if (user.ForcedPasswordResetRequired)
        {
            await signIn.SignOutAsync();
            var token = await users.GeneratePasswordResetTokenAsync(user);
            return new ResetPassword(user.Email!, token);
        }

        var roles = await users.GetRolesAsync(user);
        if (LicenceScope.IsSyncOnly(user.LicenceExpiryDate, roles, DateOnly.FromDateTime(DateTime.UtcNow)))
        {
            await audit.WriteAsync(ctx, user.Email!, user.Id, "licence-expired-sync-only");
            return new SyncOnly();
        }

        var currentTermsVersion = await db.PrivacyContent
            .Select(p => p.TermsVersion)
            .FirstOrDefaultAsync();
        if (!string.IsNullOrEmpty(currentTermsVersion)
            && (user.TermsAcceptedVersion != currentTermsVersion
                || user.TermsAcceptedLicenceExpiry != user.LicenceExpiryDate))
        {
            await signIn.SignOutAsync();
            var termsToken = await users.GenerateUserTokenAsync(user, TokenOptions.DefaultProvider, "AcceptTerms");
            return new AcceptTerms(user.Email!, termsToken);
        }

        return null;
    }
}

public static class SignInGateExtensions
{
    /// <summary>Where a gate sends the browser. The anonymous, token-protected pages re-sign the
    /// account in when they finish - through the whole pipeline again, two-factor included.</summary>
    public static IActionResult Redirect(this SignInGates.Gate gate, PageModel page, string? returnUrl) => gate switch
    {
        SignInGates.ResetPassword r => page.RedirectToPage("/Account/ResetPassword", new { email = r.Email, token = r.Token }),
        SignInGates.SyncOnly => page.RedirectToPage("/Account/FinishSync"),
        SignInGates.AcceptTerms t => page.RedirectToPage("/Account/AcceptTerms", new { email = t.Email, token = t.Token, returnUrl }),
        _ => throw new ArgumentOutOfRangeException(nameof(gate), gate, "Unknown sign-in gate."),
    };
}
