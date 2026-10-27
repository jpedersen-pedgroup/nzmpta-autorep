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
        // registered, or whether it's deactivated (avoids account enumeration). We simply don't
        // send a reset link to a deactivated account. An expired licence still gets one: it
        // blocks nothing. A short failed-attempts lockout does too, since a reset is exactly how
        // such a user recovers.
        Sent = true;
        if (string.IsNullOrWhiteSpace(Input.Email)) return Page();

        var user = await _users.FindByEmailAsync(Input.Email);
        if (user is null || AccountLockout.IsDeactivated(user.LockoutEnd)) return Page();

        var token = await _users.GeneratePasswordResetTokenAsync(user);
        var resetUrl = Url.Page("/Account/ResetPassword", null,
            new { email = user.Email, token }, Request.Scheme);
        await _email.SendEmailAsync(user.Email!,
            "Reset your MPNZ AutoRep password",
            $"<p>A password reset was requested for your account. " +
            $"<a href=\"{resetUrl}\">Click here to set a new password</a>. " +
            $"The link expires in 1 hour.</p>" +
            $"<p>If you didn't request this, you can safely ignore this email.</p>");

        return Page();
    }
}
