using Autorep.Web.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Autorep.Web.Pages.Account;

/// <summary>
/// "Which role are you signing in as?" - asked straight after sign-in of a user who is both a
/// Tester and an administrator. Each choice is a plain link to that side's home page; landing
/// there is what remembers it (<see cref="SignInRoles.RememberFromPageAsync"/>).
/// </summary>
[Authorize]
public class ChooseRoleModel : PageModel
{
    public string AdminLabel { get; private set; } = "";
    public bool IsSuperAdmin { get; private set; }

    public IActionResult OnGet()
    {
        if (!SignInRoles.HasBoth(User)) return Redirect("/");
        AdminLabel = SignInRoles.AdminLabel(User);
        IsSuperAdmin = User.IsInRole(Roles.SuperAdministrator);
        return Page();
    }
}
