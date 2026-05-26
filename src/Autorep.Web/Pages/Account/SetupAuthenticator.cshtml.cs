using System.Text;
using System.Text.Encodings.Web;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Autorep.Web.Pages.Account;

public class SetupAuthenticatorModel : PageModel
{
    private readonly UserManager<Tester> _users;
    private readonly UrlEncoder _urlEncoder;
    private const string AuthenticatorUriFormat = "otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6";

    public SetupAuthenticatorModel(UserManager<Tester> users, UrlEncoder urlEncoder)
    {
        _users = users;
        _urlEncoder = urlEncoder;
    }

    public string SharedKey { get; set; } = string.Empty;
    public string AuthenticatorUri { get; set; } = string.Empty;

    [BindProperty]
    public string Code { get; set; } = string.Empty;
    public List<string> Errors { get; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Forbid();
        await LoadAsync(user);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Forbid();

        var verificationCode = Code.Replace(" ", string.Empty).Replace("-", string.Empty);
        var isValid = await _users.VerifyTwoFactorTokenAsync(
            user, _users.Options.Tokens.AuthenticatorTokenProvider, verificationCode);

        if (!isValid)
        {
            Errors.Add("That code didn't match. Try again — make sure the device clock is correct.");
            await LoadAsync(user);
            return Page();
        }

        await _users.SetTwoFactorEnabledAsync(user, true);
        return RedirectToPage("/Account/RecoveryCodes");
    }

    private async Task LoadAsync(Tester user)
    {
        var key = await _users.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            await _users.ResetAuthenticatorKeyAsync(user);
            key = await _users.GetAuthenticatorKeyAsync(user);
        }
        SharedKey = FormatKey(key!);
        AuthenticatorUri = string.Format(
            AuthenticatorUriFormat,
            _urlEncoder.Encode("NZMPTA AutoRep"),
            _urlEncoder.Encode(user.Email!),
            key);
    }

    private static string FormatKey(string key)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < key.Length; i += 4)
            sb.Append(key, i, Math.Min(4, key.Length - i)).Append(' ');
        return sb.ToString().Trim();
    }
}
