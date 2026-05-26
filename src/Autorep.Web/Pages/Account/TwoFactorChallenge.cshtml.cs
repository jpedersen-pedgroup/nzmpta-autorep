using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Autorep.Web.Pages.Account;

[AllowAnonymous]
public class TwoFactorChallengeModel : PageModel
{
    private readonly SignInManager<Tester> _signIn;

    public TwoFactorChallengeModel(SignInManager<Tester> signIn) => _signIn = signIn;

    [BindProperty]
    public InputModel Input { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public string? ReturnUrl { get; set; }

    public class InputModel
    {
        public string Code { get; set; } = string.Empty;
        public bool RememberThisDevice { get; set; }
    }

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

        var code = Input.Code.Replace(" ", string.Empty).Replace("-", string.Empty);
        var result = await _signIn.TwoFactorAuthenticatorSignInAsync(
            code, isPersistent: false, rememberClient: Input.RememberThisDevice);

        if (result.Succeeded)
        {
            return LocalRedirect(returnUrl ?? "/");
        }
        if (result.IsLockedOut)
        {
            ErrorMessage = "Account locked.";
            return Page();
        }
        ErrorMessage = "Invalid code. Try again — make sure your device clock is correct.";
        return Page();
    }
}
