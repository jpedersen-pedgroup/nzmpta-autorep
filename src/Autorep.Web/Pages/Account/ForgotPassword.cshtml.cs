using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Autorep.Web.Pages.Account;

[AllowAnonymous]
public class ForgotPasswordModel : PageModel
{
    private readonly UserManager<Tester> _users;
    private readonly IEmailSender _email;

    public ForgotPasswordModel(UserManager<Tester> users, IEmailSender email)
    {
        _users = users;
        _email = email;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();
    public bool Sent { get; set; }

    public class InputModel
    {
        public string Email { get; set; } = string.Empty;
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        // Always show the same generic confirmation — never disclose whether an address is
        // registered, or whether it's active/inactive (avoids account enumeration). We simply
        // don't send a reset link to inactive (deactivated or licence-expired) accounts.
        Sent = true;
        if (string.IsNullOrWhiteSpace(Input.Email)) return Page();

        var user = await _users.FindByEmailAsync(Input.Email);
        if (user is null || await IsInactiveAsync(user)) return Page();

        var token = await _users.GeneratePasswordResetTokenAsync(user);
        var resetUrl = Url.Page("/Account/ResetPassword", null,
            new { email = user.Email, token }, Request.Scheme);
        await _email.SendEmailAsync(user.Email!,
            "Reset your NZMPTA AutoRep password",
            $"<p>A password reset was requested for your account. " +
            $"<a href=\"{resetUrl}\">Click here to set a new password</a>. " +
            $"The link expires in 1 hour.</p>" +
            $"<p>If you didn't request this, you can safely ignore this email.</p>");

        return Page();
    }

    // An account is "inactive" if it's been deactivated (admin lockout sentinel) or — for a pure
    // Tester — its licence has expired. A short failed-attempts lockout is NOT inactive: a reset is
    // exactly how such a user recovers. Multi-role admins keep access even with an expired licence,
    // mirroring the login pre-check.
    private async Task<bool> IsInactiveAsync(Tester user)
    {
        if (AccountLockout.IsDeactivated(user.LockoutEnd))
            return true;

        var roles = await _users.GetRolesAsync(user);
        var isPureTester = roles.Contains(Roles.Tester)
            && !roles.Contains(Roles.SuperAdministrator)
            && !roles.Contains(Roles.CompanyAdministrator);
        return isPureTester
            && user.LicenceExpiryDate is { } expiry
            && expiry < DateOnly.FromDateTime(DateTime.UtcNow);
    }
}
