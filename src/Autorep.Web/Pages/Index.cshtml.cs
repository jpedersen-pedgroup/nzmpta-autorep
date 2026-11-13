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

        // A Tester who is also an administrator picks a side once per sign-in, then comes back
        // to it - the installed app opens "/" every time it starts.
        if (SignInRoles.HasBoth(User))
            return SignInRoles.Remembered(Request) is { } side
                ? Redirect(SignInRoles.HomePath(side))
                : RedirectToPage("/Account/ChooseRole");

        if (User.IsInRole(Roles.SuperAdministrator) || User.IsInRole(Roles.CompanyAdministrator))
            return Redirect("/Admin");

        if (User.IsInRole(Roles.Tester))
            return Redirect("/App");

        return Redirect("/Account/AccessDenied");
    }
}
