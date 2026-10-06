using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Autorep.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Autorep.Web.Pages.Account;

[AllowAnonymous]
public class LoginModel : PageModel
{
    private readonly SignInManager<Tester> _signIn;
    private readonly UserManager<Tester> _users;
    private readonly SignInGates _gates;
    private readonly LoginAudit _audit;

    public LoginModel(
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
    public InputModel Input { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public string? ReturnUrl { get; set; }

    public class InputModel
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool RememberMe { get; set; }
    }

    public async Task OnGetAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
        if (!ModelState.IsValid) return Page();

        var user = await _users.FindByEmailAsync(Input.Email);

        var result = await _signIn.PasswordSignInAsync(
            Input.Email, Input.Password, Input.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            await _audit.WriteAsync(HttpContext, Input.Email, user?.Id, "success");

            // Forced reset, lapsed licence, stale terms - the checks a password alone doesn't
            // settle. Shared with the two-factor pages, which reach the same point by the code.
            if (user is not null && await _gates.CheckAsync(HttpContext, user) is { } gate)
                return gate.Redirect(this, returnUrl);

            return LocalRedirect(returnUrl ?? "/");
        }
        if (result.RequiresTwoFactor)
        {
            // Identity has stored the half-signed-in user in its own cookie; the challenge page
            // picks it up from there. Nothing about the account is on the URL.
            await _audit.WriteAsync(HttpContext, Input.Email, user?.Id, "2fa-required");
            return RedirectToPage("/Account/TwoFactorChallenge", new { returnUrl, rememberMe = Input.RememberMe });
        }
        if (result.IsLockedOut)
        {
            await _audit.WriteAsync(HttpContext, Input.Email, user?.Id, "locked-out");
            // Identity reports lockout BEFORE checking the password, so only disclose WHY (deactivated
            // vs a temporary failed-attempts lockout) to someone who actually has the password —
            // otherwise the distinct message lets an attacker enumerate deactivated accounts.
            if (user is not null && await _users.CheckPasswordAsync(user, Input.Password))
            {
                ErrorMessage = AccountLockout.IsDeactivated(user.LockoutEnd)
                    ? "This account isn't active. Contact NZMPTA."
                    : "Too many attempts — your account is temporarily locked. Try again shortly.";
            }
            else
            {
                ErrorMessage = "Invalid email or password.";
            }
            return Page();
        }

        await _audit.WriteAsync(HttpContext, Input.Email, user?.Id, "failed");
        ErrorMessage = "Invalid email or password.";
        return Page();
    }
}
