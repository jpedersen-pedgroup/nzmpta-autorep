using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Autorep.Web.Domain;
using FluentAssertions;

namespace Autorep.Web.Tests;

/// <summary>
/// Two-factor authentication end to end through the real cookie pipeline: enrolment is forced
/// on Super-Administrators, the code is demanded at sign-in, a trusted device holds for 30 days
/// and no longer than the security stamp, recovery codes get in once, and the post-sign-in
/// gates run after the code as they do after the password.
/// </summary>
public class TwoFactorSignInTests : IClassFixture<CookieAuthWebAppFactory>
{
    private const string Password = "Correct-Horse-Battery-1";
    private readonly CookieAuthWebAppFactory _factory;

    public TwoFactorSignInTests(CookieAuthWebAppFactory factory) => _factory = factory;

    private static string Email(string tag) => $"{tag}-{Guid.NewGuid():N}@example.test";

    [Fact]
    public async Task Super_admin_without_two_factor_is_held_at_setup_until_enrolled()
    {
        var email = Email("sa-unenrolled");
        var (user, _) = await _factory.CreateUserAsync(email, Password, [Roles.SuperAdministrator]);
        var browser = _factory.CreateBrowser();

        // Password alone signs in (there is no authenticator yet)...
        var login = await browser.LoginAsync(email, Password);
        login.StatusCode.Should().Be(HttpStatusCode.Redirect);
        login.Location().Should().Be("/");

        // ...but every admin page bounces to set-up, the API refuses, and the account pages work.
        (await browser.GetAsync("/Admin")).Location().Should().Be("/Account/SetupAuthenticator");
        (await browser.GetAsync("/")).Location().Should().Be("/Account/SetupAuthenticator");
        var api = await browser.GetAsync("/api/tests");
        api.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await api.Content.ReadAsStringAsync()).Should().Contain("two-factor-enrolment-required");
        (await browser.GetAsync("/Account/Manage")).StatusCode.Should().Be(HttpStatusCode.OK);

        // The set-up page says why, shows a QR code, and offers no Cancel.
        var setup = await browser.GetAsync("/Account/SetupAuthenticator");
        setup.StatusCode.Should().Be(HttpStatusCode.OK);
        var setupHtml = await setup.Content.ReadAsStringAsync();
        setupHtml.Should().Contain("required for Super Administrator accounts");
        setupHtml.Should().Contain("<svg");
        setupHtml.Should().NotContain(">Cancel<");

        // Enrol with a code from "the app".
        var key = await _factory.WithUserAsync(user.Id, (u, t) => u.GetAuthenticatorKeyAsync(t));
        var enrol = await browser.PostFormAsync("/Account/SetupAuthenticator",
            new Dictionary<string, string> { ["Code"] = Totp.Now(key!) });
        enrol.StatusCode.Should().Be(HttpStatusCode.Redirect);
        enrol.Location().Should().Be("/Account/RecoveryCodes");

        // Recovery codes shown once; ten of them exist.
        var codesPage = await browser.GetAsync("/Account/RecoveryCodes");
        codesPage.StatusCode.Should().Be(HttpStatusCode.OK);
        (await codesPage.Content.ReadAsStringAsync()).Should().Contain("Save these codes immediately");
        (await _factory.WithUserAsync(user.Id, (u, t) => u.CountRecoveryCodesAsync(t))).Should().Be(10);
        // A refresh has nothing to show and does not mint a new set.
        (await browser.GetAsync("/Account/RecoveryCodes")).Location().Should().Be("/Account/Manage");
        (await _factory.WithUserAsync(user.Id, (u, t) => u.CountRecoveryCodesAsync(t))).Should().Be(10);

        // The hold is released in the same session, without signing in again.
        (await browser.GetAsync("/Admin")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Enrolled_account_needs_the_code_and_a_trusted_device_holds_for_30_days()
    {
        var email = Email("sa-enrolled");
        var (_, key) = await _factory.CreateUserAsync(email, Password, [Roles.SuperAdministrator], twoFactor: true);
        var browser = _factory.CreateBrowser();

        var login = await browser.LoginAsync(email, Password);
        login.StatusCode.Should().Be(HttpStatusCode.Redirect);
        login.Location().Should().StartWith("/Account/TwoFactorChallenge");
        // Half signed in is not signed in.
        (await browser.GetAsync("/Admin")).Location().Should().StartWith("/Account/Login");

        var wrong = await browser.SubmitCodeAsync(Totp.Wrong(key!));
        wrong.StatusCode.Should().Be(HttpStatusCode.OK);
        (await wrong.Content.ReadAsStringAsync()).Should().Contain("Invalid code");

        var right = await browser.SubmitCodeAsync(Totp.Now(key!), trustDevice: true);
        right.StatusCode.Should().Be(HttpStatusCode.Redirect);
        right.Location().Should().Be("/");

        var trust = right.SetCookie("Identity.TwoFactorRememberMe");
        trust.Should().NotBeNull("trusting the device should set Identity's remember-me cookie");
        var expires = DateTimeOffset.Parse(Regex.Match(trust!, "expires=([^;]+)", RegexOptions.IgnoreCase).Groups[1].Value);
        (expires - DateTimeOffset.UtcNow).Should().BeCloseTo(MfaPolicy.TrustedDeviceLifetime, TimeSpan.FromHours(1));
        trust.Should().ContainEquivalentOf("httponly").And.ContainEquivalentOf("secure");

        (await browser.GetAsync("/Admin")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Same device, next sign-in: no code asked within the 30 days.
        (await browser.LogoutAsync()).StatusCode.Should().Be(HttpStatusCode.Redirect);
        var again = await browser.LoginAsync(email, Password);
        again.Location().Should().Be("/");
        (await browser.GetAsync("/Admin")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Force_logout_withdraws_the_trust_from_every_device()
    {
        var email = Email("sa-forced-out");
        var (user, key) = await _factory.CreateUserAsync(email, Password, [Roles.SuperAdministrator], twoFactor: true);
        var browser = _factory.CreateBrowser();

        await browser.LoginAsync(email, Password);
        (await browser.SubmitCodeAsync(Totp.Now(key!), trustDevice: true)).Location().Should().Be("/");
        (await browser.GetAsync("/Admin")).StatusCode.Should().Be(HttpStatusCode.OK);

        // What the admin "Force sign out" and "Reset two-factor" buttons do.
        await _factory.WithUserAsync(user.Id, (u, t) => u.UpdateSecurityStampAsync(t));

        // The session is gone, and so is the device's standing: the next sign-in wants a code.
        (await browser.GetAsync("/Admin")).Location().Should().StartWith("/Account/Login");
        var login = await browser.LoginAsync(email, Password);
        login.Location().Should().StartWith("/Account/TwoFactorChallenge");
    }

    [Fact]
    public async Task A_recovery_code_signs_in_once_and_lands_on_account_settings()
    {
        var email = Email("sa-recovery");
        var (user, _) = await _factory.CreateUserAsync(email, Password, [Roles.SuperAdministrator], twoFactor: true);
        var codes = (await _factory.WithUserAsync(user.Id, (u, t) => u.GenerateNewTwoFactorRecoveryCodesAsync(t, 10)))!.ToList();
        var browser = _factory.CreateBrowser();

        (await browser.LoginAsync(email, Password)).Location().Should().StartWith("/Account/TwoFactorChallenge");
        (await browser.GetAsync("/Account/TwoFactorRecovery")).StatusCode.Should().Be(HttpStatusCode.OK);

        var used = await browser.PostFormAsync("/Account/TwoFactorRecovery", new Dictionary<string, string> { ["Code"] = codes[0] });
        used.StatusCode.Should().Be(HttpStatusCode.Redirect);
        used.Location().Should().StartWith("/Account/Manage");
        used.SetCookie("Identity.TwoFactorRememberMe").Should().BeNull("a recovery sign-in never trusts the device");

        var manage = await browser.GetAsync(used.Location()!);
        (await manage.Content.ReadAsStringAsync()).Should().Contain("signed in with a recovery code").And.Contain("<strong>9</strong> recovery codes left");
        (await browser.GetAsync("/Admin")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Single use.
        await browser.LogoutAsync();
        await browser.LoginAsync(email, Password);
        var reused = await browser.PostFormAsync("/Account/TwoFactorRecovery", new Dictionary<string, string> { ["Code"] = codes[0] });
        reused.StatusCode.Should().Be(HttpStatusCode.OK);
        (await reused.Content.ReadAsStringAsync()).Should().Contain("Each code works once");
        (await browser.GetAsync("/Admin")).Location().Should().StartWith("/Account/Login");
    }

    [Fact]
    public async Task Forced_password_reset_still_applies_after_the_code()
    {
        var email = Email("sa-migrated");
        var (_, key) = await _factory.CreateUserAsync(email, Password, [Roles.SuperAdministrator],
            twoFactor: true, configure: u => u.ForcedPasswordResetRequired = true);
        var browser = _factory.CreateBrowser();

        (await browser.LoginAsync(email, Password)).Location().Should().StartWith("/Account/TwoFactorChallenge");
        var after = await browser.SubmitCodeAsync(Totp.Now(key!));
        after.StatusCode.Should().Be(HttpStatusCode.Redirect);
        after.Location().Should().StartWith("/Account/ResetPassword?");
        // Signed back out until the password is changed.
        (await browser.GetAsync("/Admin")).Location().Should().StartWith("/Account/Login");
    }

    [Fact]
    public async Task Required_roles_cannot_switch_two_factor_off_but_a_tester_can()
    {
        var saEmail = Email("sa-keep");
        var (sa, saKey) = await _factory.CreateUserAsync(saEmail, Password, [Roles.SuperAdministrator], twoFactor: true);
        var saBrowser = _factory.CreateBrowser();
        await saBrowser.LoginAsync(saEmail, Password);
        await saBrowser.SubmitCodeAsync(Totp.Now(saKey!));

        var managePage = await (await saBrowser.GetAsync("/Account/Manage")).Content.ReadAsStringAsync();
        managePage.Should().NotContain("Disable two-factor").And.Contain("can't be switched off");

        var refused = await saBrowser.PostFormAsync("/Account/Manage", new Dictionary<string, string>(),
            postUrl: "/Account/Manage?handler=DisableTwoFactor");
        refused.StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await _factory.WithUserAsync(sa.Id, (u, t) => u.GetTwoFactorEnabledAsync(t))).Should().BeTrue();
        // Razor HTML-encodes the apostrophe in "can't", so match up to it.
        (await (await saBrowser.GetAsync(refused.Location()!)).Content.ReadAsStringAsync())
            .Should().Contain("Two-factor authentication is required for Super Administrator accounts and can");

        var testerEmail = Email("tester-optional");
        var (tester, testerKey) = await _factory.CreateUserAsync(testerEmail, Password, [Roles.Tester], twoFactor: true);
        var testerBrowser = _factory.CreateBrowser();
        await testerBrowser.LoginAsync(testerEmail, Password);
        await testerBrowser.SubmitCodeAsync(Totp.Now(testerKey!));
        var allowed = await testerBrowser.PostFormAsync("/Account/Manage", new Dictionary<string, string>(),
            postUrl: "/Account/Manage?handler=DisableTwoFactor");
        allowed.StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await _factory.WithUserAsync(tester.Id, (u, t) => u.GetTwoFactorEnabledAsync(t))).Should().BeFalse();
    }

    [Fact]
    public async Task Api_login_never_mints_a_token_for_an_account_with_or_owing_two_factor()
    {
        var enrolledTester = Email("api-2fa-tester");
        await _factory.CreateUserAsync(enrolledTester, Password, [Roles.Tester], twoFactor: true);
        var unenrolledAdmin = Email("api-sa");
        await _factory.CreateUserAsync(unenrolledAdmin, Password, [Roles.SuperAdministrator]);
        var plainTester = Email("api-plain-tester");
        await _factory.CreateUserAsync(plainTester, Password, [Roles.Tester]);
        var client = _factory.CreateBrowser();

        foreach (var email in new[] { enrolledTester, unenrolledAdmin })
        {
            var refused = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
            refused.StatusCode.Should().Be(HttpStatusCode.Forbidden, email);
            (await refused.Content.ReadAsStringAsync()).Should().Contain("two-factor-required");
        }

        var ok = await client.PostAsJsonAsync("/api/auth/login", new { email = plainTester, password = Password });
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Replacing_the_authenticator_is_an_explicit_step_and_the_old_one_works_until_then()
    {
        var email = Email("sa-new-phone");
        var (user, oldKey) = await _factory.CreateUserAsync(email, Password, [Roles.SuperAdministrator], twoFactor: true);
        await _factory.WithUserAsync(user.Id, (u, t) => u.GenerateNewTwoFactorRecoveryCodesAsync(t, 10));
        var browser = _factory.CreateBrowser();
        await browser.LoginAsync(email, Password);
        await browser.SubmitCodeAsync(Totp.Now(oldKey!));

        // Opening (or refreshing, or prefetching) the page changes nothing and shows no key.
        for (var i = 0; i < 2; i++)
        {
            var confirm = await browser.GetAsync("/Account/SetupAuthenticator");
            confirm.StatusCode.Should().Be(HttpStatusCode.OK);
            var html = await confirm.Content.ReadAsStringAsync();
            // (The layout has inline SVG icons of its own; the QR code sits in .qr-code.)
            html.Should().Contain("Replace authenticator").And.NotContain("qr-code").And.NotContain("Verify and enable");
        }
        (await _factory.WithUserAsync(user.Id, (u, t) => u.GetAuthenticatorKeyAsync(t))).Should().Be(oldKey);
        (await _factory.WithUserAsync(user.Id, (u, t) => u.GetTwoFactorEnabledAsync(t))).Should().BeTrue();

        // The explicit step: the old key goes, two-factor is off until the new app verifies, and a
        // Super-Administrator is held on the page meanwhile.
        var replace = await browser.PostFormAsync("/Account/SetupAuthenticator", new Dictionary<string, string>(),
            postUrl: "/Account/SetupAuthenticator?handler=Replace");
        replace.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var newKey = await _factory.WithUserAsync(user.Id, (u, t) => u.GetAuthenticatorKeyAsync(t));
        newKey.Should().NotBeNull().And.NotBe(oldKey);
        (await _factory.WithUserAsync(user.Id, (u, t) => u.GetTwoFactorEnabledAsync(t))).Should().BeFalse();
        (await browser.GetAsync("/Admin")).Location().Should().Be("/Account/SetupAuthenticator");

        var setup = await browser.GetAsync("/Account/SetupAuthenticator");
        var setupHtml = await setup.Content.ReadAsStringAsync();
        setupHtml.Should().Contain("<svg").And.Contain("previous authenticator and recovery codes no longer work");

        // The old app is dead, the new one gets in; recovery codes are reissued.
        (await browser.PostFormAsync("/Account/SetupAuthenticator", new Dictionary<string, string> { ["Code"] = Totp.Now(oldKey!) }))
            .StatusCode.Should().Be(HttpStatusCode.OK, "the old key must not verify");
        var done = await browser.PostFormAsync("/Account/SetupAuthenticator", new Dictionary<string, string> { ["Code"] = Totp.Now(newKey!) });
        done.Location().Should().Be("/Account/RecoveryCodes");
        (await _factory.WithUserAsync(user.Id, (u, t) => u.GetTwoFactorEnabledAsync(t))).Should().BeTrue();
        (await _factory.WithUserAsync(user.Id, (u, t) => u.CountRecoveryCodesAsync(t))).Should().Be(10);
        (await browser.GetAsync("/Admin")).StatusCode.Should().Be(HttpStatusCode.OK);

        await browser.LogoutAsync();
        (await browser.LoginAsync(email, Password)).Location().Should().StartWith("/Account/TwoFactorChallenge");
        (await browser.SubmitCodeAsync(Totp.Now(newKey!))).Location().Should().Be("/");
    }
}
