using Autorep.Web.Domain.Entities;
using Autorep.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Autorep.Web.Pages.Account;

/// <summary>
/// Second step of sign-in for an account with an authenticator app. Identity parks the account
/// in its two-factor cookie after the password; this page turns a correct code into the real
/// session. A trusted device skips this page for <see cref="Domain.MfaPolicy.TrustedDeviceLifetime"/>,
/// and loses that trust the moment the account's security stamp changes (force-logout,
/// password reset, two-factor reset).
/// </summary>
[AllowAnonymous]
public class TwoFactorChallengeModel : PageModel
{
    private readonly SignInManager<Tester> _signIn;
    private readonly SignInGates _gates;
    private readonly LoginAudit _audit;

    public TwoFactorChallengeModel(SignInManager<Tester> signIn, SignInGates gates, LoginAudit audit)
    {
        _signIn = signIn;
        _gates = gates;
        _audit = audit;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public string? ReturnUrl { get; set; }
    public bool RememberMe { get; set; }

    public class InputModel
    {
        public string Code { get; set; } = string.Empty;
        public bool RememberThisDevice { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(string? returnUrl = null, bool rememberMe = false)
    {
        var user = await _signIn.GetTwoFactorAuthenticationUserAsync();
        if (user is null) return RedirectToPage("/Account/Login");
        ReturnUrl = returnUrl;
        RememberMe = rememberMe;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null, bool rememberMe = false)
    {
        var user = await _signIn.GetTwoFactorAuthenticationUserAsync();
        if (user is null) return RedirectToPage("/Account/Login");
        ReturnUrl = returnUrl;
        RememberMe = rememberMe;

        var code = Input.Code.Replace(" ", string.Empty).Replace("-", string.Empty);
        var result = await _signIn.TwoFactorAuthenticatorSignInAsync(
            code, isPersistent: rememberMe, rememberClient: Input.RememberThisDevice);

        if (result.Succeeded)
        {
            await _audit.WriteAsync(HttpContext, user.Email!, user.Id,
                Input.RememberThisDevice ? "2fa-success-device-trusted" : "2fa-success");

            if (await _gates.CheckAsync(HttpContext, user) is { } gate)
                return gate.Redirect(this, returnUrl);

            return LocalRedirect(returnUrl ?? "/");
        }
        if (result.IsLockedOut)
        {
            await _audit.WriteAsync(HttpContext, user.Email!, user.Id, "2fa-locked-out");
            ErrorMessage = "Too many attempts — your account is temporarily locked. Try again shortly.";
            return Page();
        }

        await _audit.WriteAsync(HttpContext, user.Email!, user.Id, "2fa-failed");
        ErrorMessage = "Invalid code. Try again — make sure your device clock is correct.";
        return Page();
    }
}
