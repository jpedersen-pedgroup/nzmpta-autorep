using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Autorep.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Autorep.Web.Pages.Account;

[Authorize]
public class ManageModel : PageModel
{
    private readonly UserManager<Tester> _users;
    private readonly SignInManager<Tester> _signIn;
    private readonly LoginAudit _audit;

    public ManageModel(UserManager<Tester> users, SignInManager<Tester> signIn, LoginAudit audit)
    {
        _users = users;
        _signIn = signIn;
        _audit = audit;
    }

    public Tester? Account { get; private set; }
    public bool TwoFactorEnabled { get; private set; }
    /// <summary>This account's role requires two-factor: it can be re-enrolled, never switched off.</summary>
    public bool TwoFactorRequired { get; private set; }
    public int RecoveryCodesLeft { get; private set; }
    /// <summary>Arrived here by signing in with a recovery code.</summary>
    public bool Recovered { get; private set; }

    [TempData]
    public string? Message { get; set; }
    [TempData]
    public string? Error { get; set; }

    public async Task<IActionResult> OnGetAsync(bool recovered = false)
    {
        Account = await _users.GetUserAsync(User);
        if (Account is null) return Forbid();
        Recovered = recovered;
        await LoadAsync(Account);
        return Page();
    }

    public async Task<IActionResult> OnPostDisableTwoFactorAsync()
    {
        var account = await _users.GetUserAsync(User);
        if (account is null) return Forbid();

        if (MfaPolicy.IsRequiredFor(await _users.GetRolesAsync(account)))
        {
            // The button isn't rendered for these roles; this covers a stale page or a crafted POST.
            Error = "Two-factor authentication is required for Super Administrator accounts and can't be switched off. To move to a new phone, set up a new authenticator instead.";
            return RedirectToPage();
        }

        await _users.SetTwoFactorEnabledAsync(account, false);
        await _users.ResetAuthenticatorKeyAsync(account);
        // Both roll the security stamp; keep this session signed in with the new one.
        await _signIn.RefreshSignInAsync(account);
        await _audit.WriteAsync(HttpContext, account.Email!, account.Id, "2fa-disabled");
        Message = "Two-factor authentication disabled.";
        return RedirectToPage();
    }

    private async Task LoadAsync(Tester account)
    {
        TwoFactorEnabled = await _users.GetTwoFactorEnabledAsync(account);
        TwoFactorRequired = MfaPolicy.IsRequiredFor(await _users.GetRolesAsync(account));
        RecoveryCodesLeft = TwoFactorEnabled ? await _users.CountRecoveryCodesAsync(account) : 0;
    }
}
