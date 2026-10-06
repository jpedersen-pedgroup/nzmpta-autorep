using Autorep.Web.Domain.Entities;
using Autorep.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Autorep.Web.Pages.Account;

/// <summary>
/// The way back in without the authenticator app: one of the single-use recovery codes issued
/// at set-up. Reached only from the challenge page, so the password has already been given.
/// A recovery sign-in never trusts the device, and lands on Account settings so the tester can
/// set the authenticator up again while they're thinking about it.
/// </summary>
[AllowAnonymous]
public class TwoFactorRecoveryModel : PageModel
{
    private readonly SignInManager<Tester> _signIn;
    private readonly UserManager<Tester> _users;
    private readonly SignInGates _gates;
    private readonly LoginAudit _audit;

    public TwoFactorRecoveryModel(
        SignInManager<Tester> signIn,
        UserManager<Tester> users,
        SignInGates gates,
        LoginAudit audit)
    {
        _signIn = signIn;
        _users = users;
        _gates = gates;
        _audit = audit;
    }

    [BindProperty]
    public string Code { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public string? ReturnUrl { get; set; }

    public async Task<IActionResult> OnGetAsync(string? returnUrl = null)
    {
        var user = await _signIn.GetTwoFactorAuthenticationUserAsync();
        if (user is null) return RedirectToPage("/Account/Login");
        ReturnUrl = returnUrl;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        var user = await _signIn.GetTwoFactorAuthenticationUserAsync();
        if (user is null) return RedirectToPage("/Account/Login");
        ReturnUrl = returnUrl;

        // Spaces only: Identity issues the codes WITH their dash and compares them verbatim.
        var code = Code.Replace(" ", string.Empty);
        var result = await _signIn.TwoFactorRecoveryCodeSignInAsync(code);

        if (result.Succeeded)
        {
            var left = await _users.CountRecoveryCodesAsync(user);
            await _audit.WriteAsync(HttpContext, user.Email!, user.Id, $"2fa-recovery-code ({left} left)");

            if (await _gates.CheckAsync(HttpContext, user) is { } gate)
                return gate.Redirect(this, returnUrl);

            return RedirectToPage("/Account/Manage", new { recovered = true });
        }
        if (result.IsLockedOut)
        {
            await _audit.WriteAsync(HttpContext, user.Email!, user.Id, "2fa-locked-out");
            ErrorMessage = "Too many attempts — your account is temporarily locked. Try again shortly.";
            return Page();
        }

        await _audit.WriteAsync(HttpContext, user.Email!, user.Id, "2fa-recovery-failed");
        ErrorMessage = "That recovery code isn't valid. Each code works once — try another.";
        return Page();
    }
}
