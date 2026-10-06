using Autorep.Web.Domain.Entities;
using Autorep.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Autorep.Web.Pages.Account;

/// <summary>
/// Shows recovery codes exactly once, straight after they're generated: by enrolment, or by the
/// Regenerate button on Account settings. Generating is a POST - the old page minted a fresh set
/// on every GET, so a refresh, a prefetch or a back-button silently invalidated the codes the
/// tester had just written down.
/// </summary>
public class RecoveryCodesModel : PageModel
{
    private readonly UserManager<Tester> _users;
    private readonly SignInManager<Tester> _signIn;
    private readonly LoginAudit _audit;

    public RecoveryCodesModel(UserManager<Tester> users, SignInManager<Tester> signIn, LoginAudit audit)
    {
        _users = users;
        _signIn = signIn;
        _audit = audit;
    }

    [TempData]
    public string[]? RecoveryCodes { get; set; }

    public IReadOnlyList<string> Codes => RecoveryCodes ?? [];

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Forbid();
        // Nothing freshly generated to show: the codes are not stored in a readable form, so
        // there is nothing to re-display. Settings offers Regenerate.
        if (RecoveryCodes is null || RecoveryCodes.Length == 0)
            return RedirectToPage("/Account/Manage");
        return Page();
    }

    public async Task<IActionResult> OnPostRegenerateAsync()
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Forbid();
        if (!await _users.GetTwoFactorEnabledAsync(user)) return RedirectToPage("/Account/Manage");

        var codes = await _users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        RecoveryCodes = codes?.ToArray() ?? [];
        // New codes roll the security stamp; keep this session (the cookie carries the stamp).
        await _signIn.RefreshSignInAsync(user);
        await _audit.WriteAsync(HttpContext, user.Email!, user.Id, "2fa-recovery-codes-regenerated");
        return RedirectToPage();
    }
}
