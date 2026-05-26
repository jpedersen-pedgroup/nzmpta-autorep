using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Autorep.Web.Pages.Account;

[Authorize]
public class ManageModel : PageModel
{
    private readonly UserManager<Tester> _users;

    public ManageModel(UserManager<Tester> users) => _users = users;

    public Tester? Account { get; private set; }
    public bool TwoFactorEnabled { get; private set; }
    public string? Message { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        Account = await _users.GetUserAsync(User);
        if (Account is null) return Forbid();
        TwoFactorEnabled = await _users.GetTwoFactorEnabledAsync(Account);
        return Page();
    }

    public async Task<IActionResult> OnPostDisableTwoFactorAsync()
    {
        var account = await _users.GetUserAsync(User);
        if (account is null) return Forbid();
        await _users.SetTwoFactorEnabledAsync(account, false);
        await _users.ResetAuthenticatorKeyAsync(account);
        Message = "Two-factor authentication disabled.";
        return RedirectToPage();
    }
}
