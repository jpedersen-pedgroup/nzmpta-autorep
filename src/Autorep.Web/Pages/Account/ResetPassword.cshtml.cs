using Autorep.Web.Domain.Entities;
using Autorep.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Autorep.Web.Pages.Account;

[AllowAnonymous]
public class ResetPasswordModel : PageModel
{
    private readonly UserManager<Tester> _users;

    public ResetPasswordModel(UserManager<Tester> users) => _users = users;

    [BindProperty]
    public InputModel Input { get; set; } = new();
    public List<string> Errors { get; } = new();
    public bool Success { get; set; }

    public class InputModel
    {
        public string Email { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public IActionResult OnGet(string? email, string? token)
    {
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
        {
            Errors.Add("The reset link is invalid or has expired.");
            return Page();
        }
        Input.Email = email;
        Input.Token = token;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(Input.Password))
        {
            Errors.Add("Password is required.");
            return Page();
        }
        if (Input.Password != Input.ConfirmPassword)
        {
            Errors.Add("Passwords do not match.");
            return Page();
        }

        var user = await _users.FindByEmailAsync(Input.Email);
        if (user is null)
        {
            Errors.Add("The reset link is invalid or has expired.");
            return Page();
        }

        // A welcome email's link carries an invitation, which outlasts a reset link (see
        // Invitation). Swap a good one for a reset token, so the first password is set by an
        // ordinary reset.
        var token = Input.Token;
        if (await _users.VerifyUserTokenAsync(user, Invitation.TokenProvider, Invitation.Purpose, token))
            token = await _users.GeneratePasswordResetTokenAsync(user);

        var result = await _users.ResetPasswordAsync(user, token, Input.Password);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors)
            {
                // Identity's own wording is "Invalid token.", which doesn't tell anyone what to do.
                Errors.Add(e.Code == nameof(IdentityErrorDescriber.InvalidToken)
                    ? "This link has expired or has already been used. Use \"Forgot your password?\" on the sign-in page to get a new one."
                    : e.Description);
            }
            return Page();
        }

        // Clear the forced-reset flag so future logins go straight through.
        if (user.ForcedPasswordResetRequired)
        {
            user.ForcedPasswordResetRequired = false;
            await _users.UpdateAsync(user);
        }

        Success = true;
        return Page();
    }
}
