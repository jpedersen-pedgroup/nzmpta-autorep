using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Autorep.Web.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Autorep.Web.Tests;

/// <summary>
/// An expired Tester licence blocks nothing (MPNZ, Oct 2026). Through the real cookie pipeline, a
/// tester whose licence has run out signs in to the whole app, resets their password, adds a farm
/// and syncs, and the token API serves them too. Also here: renewing a licence no longer asks for
/// the terms again, and each password link lasts as long as its email says.
/// </summary>
public class LicenceTests : IClassFixture<CookieAuthWebAppFactory>
{
    private const string Password = "Correct-Horse-Battery-1";
    private const string NewPassword = "Staple-Battery-Horse-2";
    private const string TermsVersion = "terms-v2";
    private readonly CookieAuthWebAppFactory _factory;

    public LicenceTests(CookieAuthWebAppFactory factory)
    {
        _factory = factory;
        // Terms in force, so the terms gate is live here (the factory seeds none).
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
        if (!db.PrivacyContent.Any())
        {
            db.PrivacyContent.Add(new PrivacyContent { TermsVersion = TermsVersion, TermsBody = "Test carefully." });
            db.SaveChanges();
        }
    }

    private static string Email(string tag) => $"{tag}-{Guid.NewGuid():N}@example.test";

    private static DateOnly Yesterday => NzTime.Today.AddDays(-1);

    private async Task<Guid> CompanyAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
        var company = new TestingCompany { Name = "Licence Co " + Guid.NewGuid() };
        db.TestingCompanies.Add(company);
        await db.SaveChangesAsync();
        return company.Id;
    }

    /// <summary>A Tester in a Testing Company, terms accepted, whose licence ran out yesterday.</summary>
    private async Task<string> ExpiredTesterAsync(string tag, Action<Tester>? configure = null)
    {
        var companyId = await CompanyAsync();
        var email = Email(tag);
        await _factory.CreateUserAsync(email, Password, [Roles.Tester], configure: u =>
        {
            u.TestingCompanyId = companyId;
            u.LicenceExpiryDate = Yesterday;
            u.TermsAcceptedVersion = TermsVersion;
            u.TermsAcceptedLicenceExpiry = Yesterday;
            configure?.Invoke(u);
        });
        return email;
    }

    private CapturingEmailSender.Sent? MailTo(string email) =>
        _factory.Services.GetRequiredService<CapturingEmailSender>().All.LastOrDefault(m => m.Email == email);

    /// <summary>The link in an email, as a path and query on this server.</summary>
    private static string LinkIn(CapturingEmailSender.Sent mail) =>
        new Uri(Regex.Match(mail.HtmlMessage, "href=\"([^\"]+)\"").Groups[1].Value).PathAndQuery;

    private static string TokenIn(string link) => QueryHelpers.ParseQuery(new Uri("https://localhost" + link).Query)["token"]!;

    private static Dictionary<string, string> PasswordForm(string email, string token, string password) => new()
    {
        ["Input.Email"] = email,
        ["Input.Token"] = token,
        ["Input.Password"] = password,
        ["Input.ConfirmPassword"] = password,
    };

    [Fact]
    public async Task An_expired_tester_signs_in_to_the_whole_app_with_a_reminder()
    {
        var email = await ExpiredTesterAsync("signin");
        var browser = _factory.CreateBrowser();

        (await browser.LoginAsync(email, Password)).Location().Should().Be("/");
        // The installed app's start_url: the tester app, not a send-only page.
        (await browser.GetAsync("/")).Location().Should().Be("/App");
        foreach (var path in new[] { "/App", "/App/Tests", "/App/Tests/New", "/App/Tests/Wizard" })
        {
            (await browser.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.OK, $"{path} stays open after the licence expires");
        }
        (await browser.GetAsync("/Account/FinishSync")).StatusCode.Should().Be(HttpStatusCode.NotFound, "the send-only page is gone");

        var page = await (await browser.GetAsync("/App/Tests")).Content.ReadAsStringAsync();
        page.Should().Contain($"Your Tester licence expired on {Yesterday:dd MMM yyyy}.")
            .And.Contain("href=\"mailto:info@milkingpumping.co.nz\"");
    }

    [Fact]
    public async Task An_expired_tester_adds_a_farm_and_syncs_a_test()
    {
        var email = await ExpiredTesterAsync("work");
        var browser = _factory.CreateBrowser();
        await browser.LoginAsync(email, Password);

        var farmName = "Expired Licence Farm " + Guid.NewGuid();
        (await browser.PostAsJsonAsync("/api/farms", new { name = farmName })).StatusCode.Should().Be(HttpStatusCode.OK);

        var push = await browser.PostAsJsonAsync("/api/sync/tests", new
        {
            clientId = Guid.NewGuid(),
            farmName,
            createdAt = DateTimeOffset.UtcNow,
        });
        push.StatusCode.Should().Be(HttpStatusCode.Created);
        (await browser.GetAsync("/api/sync/tests")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_expired_tester_resets_their_password_and_signs_in_with_it()
    {
        var email = await ExpiredTesterAsync("reset");
        var browser = _factory.CreateBrowser();

        (await browser.PostFormAsync("/Account/ForgotPassword", new Dictionary<string, string> { ["Input.Email"] = email }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var mail = MailTo(email);
        mail.Should().NotBeNull("an expired licence still gets a reset link");
        mail!.HtmlMessage.Should().Contain("The link expires in 1 hour.");

        var link = LinkIn(mail);
        var reset = await browser.PostFormAsync(link, PasswordForm(email, TokenIn(link), NewPassword));
        (await reset.Content.ReadAsStringAsync()).Should().Contain("Password updated");

        (await _factory.CreateBrowser().LoginAsync(email, NewPassword)).Location().Should().Be("/");
    }

    [Fact]
    public async Task A_deactivated_account_still_gets_no_reset_link()
    {
        var email = await ExpiredTesterAsync("deactivated", u => u.LockoutEnd = AccountLockout.DeactivatedUntil());

        await _factory.CreateBrowser().PostFormAsync("/Account/ForgotPassword", new Dictionary<string, string> { ["Input.Email"] = email });

        MailTo(email).Should().BeNull();
    }

    [Fact]
    public async Task The_token_api_serves_an_expired_tester_at_login_and_refresh()
    {
        var email = await ExpiredTesterAsync("api");
        var client = _factory.CreateBrowser();

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var refreshToken = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("refreshToken").GetString();

        (await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // MPNZ (Oct 2026) saw no reason to ask again on renewal; a new version of the terms still does.
    [Fact]
    public async Task Renewing_a_licence_does_not_ask_for_the_terms_again()
    {
        var renewed = Email("renewed");
        await _factory.CreateUserAsync(renewed, Password, [Roles.Tester], configure: u =>
        {
            u.LicenceExpiryDate = NzTime.Today.AddYears(1);
            u.TermsAcceptedVersion = TermsVersion;
            u.TermsAcceptedLicenceExpiry = NzTime.Today.AddDays(-30); // accepted under the old licence
        });
        (await _factory.CreateBrowser().LoginAsync(renewed, Password)).Location().Should().Be("/");

        var stale = Email("stale-terms");
        await _factory.CreateUserAsync(stale, Password, [Roles.Tester], configure: u =>
        {
            u.LicenceExpiryDate = NzTime.Today.AddYears(1);
            u.TermsAcceptedVersion = "terms-v1";
            u.TermsAcceptedLicenceExpiry = u.LicenceExpiryDate;
        });
        (await _factory.CreateBrowser().LoginAsync(stale, Password)).Location().Should().StartWith("/Account/AcceptTerms?");
    }

    [Fact]
    public void Reset_links_last_an_hour_and_welcome_links_a_week()
    {
        _factory.Services.GetRequiredService<IOptions<DataProtectionTokenProviderOptions>>().Value
            .TokenLifespan.Should().Be(TimeSpan.FromHours(1));
        _factory.Services.GetRequiredService<IOptions<InvitationTokenProviderOptions>>().Value
            .TokenLifespan.Should().Be(TimeSpan.FromDays(7));
    }

    [Fact]
    public async Task A_welcome_email_link_sets_the_first_password_once()
    {
        // A Super-Administrator (two-factor is required of them) creates the account.
        var adminEmail = Email("sa-invites");
        var (_, key) = await _factory.CreateUserAsync(adminEmail, Password, [Roles.SuperAdministrator],
            twoFactor: true, configure: u => u.TermsAcceptedVersion = TermsVersion);
        var admin = _factory.CreateBrowser();
        await admin.LoginAsync(adminEmail, Password);
        await admin.SubmitCodeAsync(Totp.Now(key!));

        var invitee = Email("invitee");
        var created = await admin.PostFormAsync("/Admin/Testers/New", new Dictionary<string, string>
        {
            ["Input.Email"] = invitee,
            ["Input.DisplayName"] = "New Tester",
            ["Input.SelectedRoles"] = Roles.Tester,
            ["Input.TestingCompanyId"] = (await CompanyAsync()).ToString(),
        });
        created.Location().Should().Be("/Admin/Testers");

        var mail = MailTo(invitee);
        mail!.HtmlMessage.Should().Contain("The link works for 7 days.");
        var link = LinkIn(mail);
        var token = TokenIn(link);
        using (var scope = _factory.Services.CreateScope())
        {
            // An invitation, not an hour-long reset token.
            var users = scope.ServiceProvider.GetRequiredService<UserManager<Tester>>();
            var account = await users.FindByEmailAsync(invitee);
            (await users.VerifyUserTokenAsync(account!, Invitation.TokenProvider, Invitation.Purpose, token)).Should().BeTrue();
        }

        var set = await _factory.CreateBrowser().PostFormAsync(link, PasswordForm(invitee, token, NewPassword));
        (await set.Content.ReadAsStringAsync()).Should().Contain("Password updated");
        // Signed in with it; a brand-new account has the terms to accept first.
        (await _factory.CreateBrowser().LoginAsync(invitee, NewPassword)).Location().Should().StartWith("/Account/AcceptTerms?");

        // Setting the password moved the security stamp on, so the link is spent.
        var again = await _factory.CreateBrowser().PostFormAsync(link, PasswordForm(invitee, token, "Another-Password-3"));
        (await again.Content.ReadAsStringAsync()).Should().Contain("This link has expired or has already been used.");
    }
}
