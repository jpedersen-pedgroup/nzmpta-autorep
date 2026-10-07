using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Autorep.Web.Pages.Account;

[Authorize]
public class LogoutModel : PageModel
{
    private readonly SignInManager<Tester> _signIn;

    public LogoutModel(SignInManager<Tester> signIn) => _signIn = signIn;

    // A confirmation page rather than a redirect home: the offline shell links here because it
    // has no antiforgery token to post with (see Logout.cshtml).
    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        await _signIn.SignOutAsync();
        return RedirectToPage("/Account/Login");
    }
}
