using System.Text;
using System.Text.Encodings.Web;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Autorep.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QRCoder;

namespace Autorep.Web.Pages.Account;

/// <summary>
/// Enrols an authenticator app: shows the shared key as a QR code (and as text, for a device
/// that can't scan), proves the app has it by checking one code, then turns two-factor on and
/// hands over the recovery codes. For a role that must have two-factor this is also where the
/// middleware parks the account until it's done.
/// </summary>
public class SetupAuthenticatorModel : PageModel
{
    private readonly UserManager<Tester> _users;
    private readonly SignInManager<Tester> _signIn;
    private readonly UrlEncoder _urlEncoder;
    private readonly LoginAudit _audit;
    private const string AuthenticatorUriFormat = "otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6";

    public SetupAuthenticatorModel(
        UserManager<Tester> users,
        SignInManager<Tester> signIn,
        UrlEncoder urlEncoder,
        LoginAudit audit)
    {
        _users = users;
        _signIn = signIn;
        _urlEncoder = urlEncoder;
        _audit = audit;
    }

    public string SharedKey { get; set; } = string.Empty;
    public string AuthenticatorUri { get; set; } = string.Empty;
    /// <summary>The otpauth URI as an inline SVG, for the app to scan.</summary>
    public string QrCodeSvg { get; set; } = string.Empty;
    /// <summary>True when this account's role requires two-factor - the page says so and offers
    /// no way out but sign-out.</summary>
    public bool Required { get; set; }
    /// <summary>True when an authenticator is already enrolled and this is a re-enrolment (a new
    /// phone); the old app stops working the moment the new one is verified.</summary>
    public bool Replacing { get; set; }

    [BindProperty]
    public string Code { get; set; } = string.Empty;
    public List<string> Errors { get; } = new();

    [TempData]
    public string[]? RecoveryCodes { get; set; }

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
        // Fresh codes with every enrolment: a re-enrolment after a lost phone must not leave the
        // old phone's codes valid.
        var codes = await _users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        RecoveryCodes = codes?.ToArray() ?? [];

        // The session's principal carried the "must enrol" claim; re-issue it now that this is
        // done, or the middleware keeps sending the account straight back here.
        await _signIn.RefreshSignInAsync(user);
        await _audit.WriteAsync(HttpContext, user.Email!, user.Id, "2fa-enrolled");

        return RedirectToPage("/Account/RecoveryCodes");
    }

    private async Task LoadAsync(Tester user)
    {
        Required = MfaPolicy.IsRequiredFor(await _users.GetRolesAsync(user));
        Replacing = user.TwoFactorEnabled;

        // A re-enrolment gets a new key: the one in the lost phone must stop working.
        var key = await _users.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key) || (Replacing && Request.Method == HttpMethods.Get))
        {
            await _users.ResetAuthenticatorKeyAsync(user);
            key = await _users.GetAuthenticatorKeyAsync(user);
            // Resetting the key also rolls the security stamp, which would sign this very session
            // out at the next stamp check. Re-issue the cookie with the new stamp.
            await _signIn.RefreshSignInAsync(user);
        }
        SharedKey = FormatKey(key!);
        AuthenticatorUri = string.Format(
            AuthenticatorUriFormat,
            _urlEncoder.Encode("NZMPTA AutoRep"),
            _urlEncoder.Encode(user.Email!),
            key);

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(AuthenticatorUri, QRCodeGenerator.ECCLevel.Q);
        QrCodeSvg = new SvgQRCode(data).GetGraphic(4, "#111827", "#ffffff", drawQuietZones: true);
    }

    private static string FormatKey(string key)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < key.Length; i += 4)
            sb.Append(key, i, Math.Min(4, key.Length - i)).Append(' ');
        return sb.ToString().Trim().ToLowerInvariant();
    }
}
