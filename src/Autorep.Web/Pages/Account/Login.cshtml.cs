using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Pages.Account;

[AllowAnonymous]
public class LoginModel : PageModel
{
    private readonly SignInManager<Tester> _signIn;
    private readonly UserManager<Tester> _users;
    private readonly AutorepDbContext _db;
    private readonly ILogger<LoginModel> _logger;

    public LoginModel(
        SignInManager<Tester> signIn,
        UserManager<Tester> users,
        AutorepDbContext db,
        ILogger<LoginModel> logger)
    {
        _signIn = signIn;
        _users = users;
        _db = db;
        _logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public string? ReturnUrl { get; set; }

    public class InputModel
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool RememberMe { get; set; }
    }

    public async Task OnGetAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
        if (!ModelState.IsValid) return Page();

        var user = await _users.FindByEmailAsync(Input.Email);

        // Pre-check Tester Licence so an expired Tester doesn't waste a password attempt.
        if (user is not null)
        {
            var roles = await _users.GetRolesAsync(user);
            var isPureTester = roles.Contains(Roles.Tester)
                && !roles.Contains(Roles.SuperAdministrator)
                && !roles.Contains(Roles.CompanyAdministrator);
            if (isPureTester && user.LicenceExpiryDate is { } expiry
                && expiry < DateOnly.FromDateTime(DateTime.UtcNow))
            {
                ErrorMessage = "Your tester licence has expired. Contact NZMPTA to renew.";
                await WriteLoginAuditAsync(Input.Email, user.Id, "licence-expired");
                return Page();
            }
        }

        var result = await _signIn.PasswordSignInAsync(
            Input.Email, Input.Password, Input.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            await WriteLoginAuditAsync(Input.Email, user?.Id, "success");
            // Forced password reset path: sign back out and route through ResetPassword.
            if (user is not null && user.ForcedPasswordResetRequired)
            {
                await _signIn.SignOutAsync();
                var token = await _users.GeneratePasswordResetTokenAsync(user);
                return RedirectToPage("/Account/ResetPassword",
                    new { email = user.Email, token });
            }

            // Terms acceptance gate: require (re-)acceptance when the current terms version differs
            // from what the tester accepted, or their licence has been renewed since acceptance.
            // Mirror the reset flow: sign out and route through the anonymous, token-protected page.
            if (user is not null)
            {
                var currentTermsVersion = await _db.PrivacyContent
                    .Select(p => p.TermsVersion)
                    .FirstOrDefaultAsync();
                if (!string.IsNullOrEmpty(currentTermsVersion)
                    && (user.TermsAcceptedVersion != currentTermsVersion
                        || user.TermsAcceptedLicenceExpiry != user.LicenceExpiryDate))
                {
                    await _signIn.SignOutAsync();
                    var termsToken = await _users.GenerateUserTokenAsync(
                        user, TokenOptions.DefaultProvider, "AcceptTerms");
                    return RedirectToPage("/Account/AcceptTerms",
                        new { email = user.Email, token = termsToken, returnUrl });
                }
            }

            return LocalRedirect(returnUrl ?? "/");
        }
        if (result.RequiresTwoFactor)
        {
            await WriteLoginAuditAsync(Input.Email, user?.Id, "2fa-required");
            return RedirectToPage("/Account/TwoFactorChallenge", new { returnUrl });
        }
        if (result.IsLockedOut)
        {
            await WriteLoginAuditAsync(Input.Email, user?.Id, "locked-out");
            ErrorMessage = "Account locked. Try again later.";
            return Page();
        }

        await WriteLoginAuditAsync(Input.Email, user?.Id, "failed");
        ErrorMessage = "Invalid email or password.";
        return Page();
    }

    private async Task WriteLoginAuditAsync(string email, string? userId, string outcome)
    {
        // Don't log the raw email to app logs/App Insights — the authoritative login record is the
        // audit row below (covered by the IPP3A notice + retention policy).
        _logger.LogInformation("Login {Outcome} for user {UserId}", outcome, userId ?? "(anonymous)");
        _db.AuditEntries.Add(new Autorep.Web.Domain.Entities.AuditEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Actor = userId ?? "anonymous",
            EntityType = "Login",
            EntityKey = email,
            Operation = outcome,
            AfterJson = $"{{\"ip\":\"{HttpContext.Connection.RemoteIpAddress}\",\"userAgent\":{System.Text.Json.JsonSerializer.Serialize(Request.Headers.UserAgent.ToString())}}}"
        });
        try { await _db.SaveChangesAsync(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to write login audit row"); }
    }
}
