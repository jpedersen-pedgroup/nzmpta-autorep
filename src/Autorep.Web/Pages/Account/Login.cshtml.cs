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

        var result = await _signIn.PasswordSignInAsync(
            Input.Email, Input.Password, Input.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            await WriteLoginAuditAsync(Input.Email, user?.Id, "success");

            // Forced password reset path: sign back out and route through ResetPassword. Ahead of
            // the licence branch below, so a temporary password can never be parlayed into a
            // sync-only session.
            if (user is not null && user.ForcedPasswordResetRequired)
            {
                await _signIn.SignOutAsync();
                var token = await _users.GeneratePasswordResetTokenAsync(user);
                return RedirectToPage("/Account/ResetPassword",
                    new { email = user.Email, token });
            }

            // Expired Tester licence — checked only AFTER a correct password, so it can't be used
            // to enumerate accounts. The session STANDS: TesterClaimsPrincipalFactory has already
            // marked it sync-only, so every tester surface is closed to it and the flush page is
            // all that remains. Signing them out instead would strand any capture still queued on
            // their device — only the tester it belongs to can ever push it.
            if (user is not null)
            {
                var roles = await _users.GetRolesAsync(user);
                if (LicenceScope.IsSyncOnly(user.LicenceExpiryDate, roles, DateOnly.FromDateTime(DateTime.UtcNow)))
                {
                    await WriteLoginAuditAsync(Input.Email, user.Id, "licence-expired-sync-only");
                    return RedirectToPage("/Account/FinishSync");
                }
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
            // Identity reports lockout BEFORE checking the password, so only disclose WHY (deactivated
            // vs a temporary failed-attempts lockout) to someone who actually has the password —
            // otherwise the distinct message lets an attacker enumerate deactivated accounts.
            if (user is not null && await _users.CheckPasswordAsync(user, Input.Password))
            {
                ErrorMessage = AccountLockout.IsDeactivated(user.LockoutEnd)
                    ? "This account isn't active. Contact NZMPTA."
                    : "Too many attempts — your account is temporarily locked. Try again shortly.";
            }
            else
            {
                ErrorMessage = "Invalid email or password.";
            }
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
