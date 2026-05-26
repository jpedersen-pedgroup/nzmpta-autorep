using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Autorep.Web.Pages.Account;

public class RecoveryCodesModel : PageModel
{
    private readonly UserManager<Tester> _users;

    public RecoveryCodesModel(UserManager<Tester> users) => _users = users;

    public IReadOnlyList<string> Codes { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Forbid();
        var codes = await _users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        Codes = codes?.ToList() ?? new List<string>();
        return Page();
    }
}
