using Autorep.Web.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Autorep.Web.Pages;

// Landing page redirects users to their role's home area.
// Anonymous users go to /Account/Login.
public class IndexModel : PageModel
{
    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated != true)
            return Redirect("/Account/Login");

        if (User.IsInRole(Roles.SuperAdministrator) || User.IsInRole(Roles.CompanyAdministrator))
            return Redirect("/Admin");

        if (User.IsInRole(Roles.Tester))
        {
            // A lapsed licence can't reach /App, so send them where they can still act: the page
            // that flushes tests captured before it expired. This is also the PWA's start_url.
            return Redirect(User.HasClaim(LicenceScope.ScopeClaim, LicenceScope.SyncOnly)
                ? "/Account/FinishSync"
                : "/App");
        }

        return Redirect("/Account/AccessDenied");
    }
}
