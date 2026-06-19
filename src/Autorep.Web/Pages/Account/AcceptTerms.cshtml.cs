using Autorep.Web.Data;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Pages.Account;

// Anonymous, token-protected Terms-of-Use acceptance gate. Reached from Login when a tester's
// accepted version is stale or their licence was renewed; mirrors the forced-password-reset flow
// (the tester is signed out first and arrives with a one-time token in the query string).
[AllowAnonymous]
public class AcceptTermsModel : PageModel
{
    private readonly UserManager<Tester> _users;
    private readonly AutorepDbContext _db;

    public AcceptTermsModel(UserManager<Tester> users, AutorepDbContext db)
    {
        _users = users;
        _db = db;
    }

    [BindProperty] public InputModel Input { get; set; } = new();
    public List<string> Errors { get; } = new();
    public string TermsVersion { get; private set; } = "";
    public string TermsBody { get; private set; } = "";
    public bool Success { get; private set; }

    public class InputModel
    {
        public string Email { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public string? ReturnUrl { get; set; }
        public bool Agree { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(string? email, string? token, string? returnUrl)
    {
        await LoadTermsAsync();
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
        {
            Errors.Add("This link is invalid or has expired. Please sign in again.");
            return Page();
        }
        Input.Email = email;
        Input.Token = token;
        Input.ReturnUrl = returnUrl;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadTermsAsync();

        var user = await _users.FindByEmailAsync(Input.Email);
        if (user is null
            || !await _users.VerifyUserTokenAsync(user, TokenOptions.DefaultProvider, "AcceptTerms", Input.Token))
        {
            Errors.Add("This link is invalid or has expired. Please sign in again.");
            return Page();
        }

        if (!Input.Agree)
        {
            Errors.Add("You must accept the terms to continue.");
            return Page();
        }

        user.TermsAcceptedVersion = TermsVersion;
        user.TermsAcceptedAt = DateTimeOffset.UtcNow;
        user.TermsAcceptedLicenceExpiry = user.LicenceExpiryDate;
        await _users.UpdateAsync(user);

        Success = true;
        return Page();
    }

    private async Task LoadTermsAsync()
    {
        var content = await _db.PrivacyContent
            .OrderByDescending(p => p.UpdatedAt)
            .Select(p => new { p.TermsVersion, p.TermsBody })
            .FirstOrDefaultAsync();
        TermsVersion = content?.TermsVersion ?? "";
        TermsBody = content?.TermsBody ?? "";
    }
}
