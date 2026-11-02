using System.Net;
using System.Net.Http.Json;
using Autorep.Web.Domain;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace Autorep.Web.Tests;

/// <summary>
/// The default since 3 Nov 2026, when MPNZ asked for two-factor to be off for administrators for
/// now: no role must use it. An administrator without an authenticator gets in on a password, as a
/// tester does; one with an authenticator is still asked for the code and may switch it off.
/// </summary>
public class MfaNotRequiredTests : IClassFixture<CookieAuthWebAppFactory>, IClassFixture<AuthedWebAppFactory>
{
    private const string Password = "Correct-Horse-Battery-1";
    private readonly CookieAuthWebAppFactory _factory;
    private readonly AuthedWebAppFactory _authed;

    public MfaNotRequiredTests(CookieAuthWebAppFactory factory, AuthedWebAppFactory authed)
    {
        _factory = factory;
        _authed = authed;
    }

    private static string Email(string tag) => $"{tag}-{Guid.NewGuid():N}@example.test";

    [Theory]
    [InlineData(Roles.SuperAdministrator)]
    [InlineData(Roles.CompanyAdministrator)]
    public async Task An_administrator_without_two_factor_signs_in_on_a_password(string role)
    {
        var email = Email("admin-no-2fa");
        await _factory.CreateUserAsync(email, Password, [role]);
        var browser = _factory.CreateBrowser();

        (await browser.LoginAsync(email, Password)).Location().Should().Be("/");
        (await browser.GetAsync("/Admin")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await browser.GetAsync("/api/tests")).StatusCode.Should().Be(HttpStatusCode.OK);

        // The token API serves them too: it refuses only an account with an authenticator.
        var token = await _factory.CreateBrowser().PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        token.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_super_admin_with_two_factor_is_still_asked_for_the_code_and_can_switch_it_off()
    {
        var email = Email("sa-optional");
        var (user, key) = await _factory.CreateUserAsync(email, Password, [Roles.SuperAdministrator], twoFactor: true);
        var browser = _factory.CreateBrowser();

        (await browser.LoginAsync(email, Password)).Location().Should().StartWith("/Account/TwoFactorChallenge");
        await browser.SubmitCodeAsync(Totp.Now(key!));

        var managePage = await (await browser.GetAsync("/Account/Manage")).Content.ReadAsStringAsync();
        managePage.Should().Contain("Disable two-factor").And.NotContain("can't be switched off");
        var disabled = await browser.PostFormAsync("/Account/Manage", new Dictionary<string, string>(),
            postUrl: "/Account/Manage?handler=DisableTwoFactor");
        disabled.StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await _factory.WithUserAsync(user.Id, (u, t) => u.GetTwoFactorEnabledAsync(t))).Should().BeFalse();

        (await _factory.CreateBrowser().LoginAsync(email, Password)).Location().Should().Be("/");
    }

    [Fact]
    public async Task An_administrator_session_is_not_capped_at_30_days_from_a_code()
    {
        var old = _authed.CreateClientAs(Roles.SuperAdministrator, mfaAt: DateTimeOffset.UtcNow.AddDays(-400));
        (await old.GetAsync("/Admin")).StatusCode.Should().Be(HttpStatusCode.OK);
        var unstamped = _authed.CreateClientAs(Roles.SuperAdministrator, noMfaStamp: true);
        (await unstamped.GetAsync("/Admin")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void A_required_role_that_is_not_a_role_stops_the_app_at_start_up()
    {
        using var root = new AuthedWebAppFactory();
        var misspelt = root.WithWebHostBuilder(b => b.UseSetting($"{MfaOptions.Section}:RequiredRoles:0", "SuperAdmin"));

        var start = () => misspelt.CreateClient();
        start.Should().Throw<OptionsValidationException>().WithMessage("*RequiredRoles may only list*");
    }
}
