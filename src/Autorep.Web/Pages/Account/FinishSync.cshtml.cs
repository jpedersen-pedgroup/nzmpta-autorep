using Autorep.Web.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Autorep.Web.Pages.Account;

/// <summary>
/// The only surface a lapsed Tester can reach: send Machine Tests already captured on this device.
/// Deliberately outside /App — that folder's policy excludes sync-only sessions — so it must carry
/// its own guard.
/// </summary>
[Authorize(Roles = Roles.Tester)]
public class FinishSyncModel : PageModel
{
    public IActionResult OnGet()
    {
        // A licensed tester has the whole app; don't strand them on the cut-down page.
        return User.HasClaim(LicenceScope.ScopeClaim, LicenceScope.SyncOnly)
            ? Page()
            : Redirect("/App");
    }
}
