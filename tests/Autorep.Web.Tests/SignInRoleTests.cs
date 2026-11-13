using System.Net;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Autorep.Web.Tests;

/// <summary>
/// A Tester who is also a Company or Super Administrator is asked which role they're signing in as,
/// and then sees that side's menu only, with a link to switch (Josh, 12 Nov 2026: both menus at
/// once didn't fit the bar). Through the real cookie pipeline. The choice is a menu, not a
/// permission: the other side's pages still open, and the menu follows the page.
/// </summary>
public class SignInRoleTests : IClassFixture<CookieAuthWebAppFactory>
{
    private const string Password = "Correct-Horse-Battery-1";
    private readonly CookieAuthWebAppFactory _factory;

    public SignInRoleTests(CookieAuthWebAppFactory factory) => _factory = factory;

    private static string Email(string tag) => $"{tag}-{Guid.NewGuid():N}@example.test";

    private async Task<Guid> CompanyAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
        var company = new TestingCompany { Name = "Role Co " + Guid.NewGuid() };
        db.TestingCompanies.Add(company);
        await db.SaveChangesAsync();
        return company.Id;
    }

    /// <summary>Signs in a new account in these roles and returns the browser, sitting at "/".</summary>
    private async Task<HttpClient> SignedInAsync(string[] roles, Action<Tester>? configure = null)
    {
        var companyId = await CompanyAsync();
        var email = Email(string.Join("-", roles).ToLowerInvariant());
        await _factory.CreateUserAsync(email, Password, roles, configure: u =>
        {
            u.TestingCompanyId = companyId;
            configure?.Invoke(u);
        });
        var browser = _factory.CreateBrowser();
        (await browser.LoginAsync(email, Password)).Location().Should().Be("/");
        return browser;
    }

    private static async Task<string> PageAsync(HttpClient browser, string path)
    {
        var res = await browser.GetAsync(path);
        res.StatusCode.Should().Be(HttpStatusCode.OK, $"{path} should open");
        return WebUtility.HtmlDecode(await res.Content.ReadAsStringAsync());
    }

    /// <summary>The menu bar's links, without the page body (whose tiles repeat some names).</summary>
    private static string Menu(string html)
    {
        // Not the whole tag: the layout's scoped CSS adds an attribute to it.
        var start = html.IndexOf("class=\"app-header__nav\"", StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, "a signed-in page has the menu bar");
        return html[start..html.IndexOf("</nav>", start, StringComparison.Ordinal)];
    }

    // The Admin | Tester toggle (Josh, 13 Nov 2026): in the menu bar, straight after Help.
    private static string Toggle(string html)
    {
        var menu = Menu(html);
        System.Text.RegularExpressions.Regex.IsMatch(menu, ">Help</a>\\s*<div[^>]*class=\"role-toggle\"")
            .Should().BeTrue("the toggle sits next to Help");
        var start = menu.IndexOf("class=\"role-toggle\"", StringComparison.Ordinal);
        return menu[start..menu.IndexOf("</div>", start, StringComparison.Ordinal)];
    }

    private static void ShowsTesterMenuOnly(string html)
    {
        var menu = Menu(html);
        menu.Should().Contain(">My tests<").And.Contain(">New test<");
        menu.Should().NotContain(">All tests<").And.NotContain(">Upcoming tests<");
    }

    private static void ShowsAdminMenuOnly(string html)
    {
        var menu = Menu(html);
        menu.Should().Contain(">All tests<").And.Contain(">Upcoming tests<");
        menu.Should().NotContain(">My tests<").And.NotContain(">New test<");
    }

    [Fact]
    public async Task A_tester_who_is_also_a_super_admin_is_asked_which_role_after_signing_in()
    {
        var browser = await SignedInAsync([Roles.Tester, Roles.SuperAdministrator]);

        (await browser.GetAsync("/")).Location().Should().Be("/Account/ChooseRole");
        var html = await PageAsync(browser, "/Account/ChooseRole");
        html.Should().Contain("Which role are you signing in as?")
            .And.Contain("You're a Tester and a Super Administrator.")
            .And.Contain("href=\"/App\"")
            .And.Contain("href=\"/Admin\"")
            .And.Contain("Manage tests, farms, users, companies and test standards.");
    }

    [Fact]
    public async Task Choosing_tester_shows_the_tester_menu_and_the_app_reopens_there()
    {
        var browser = await SignedInAsync([Roles.Tester, Roles.SuperAdministrator]);

        var app = await browser.GetAsync("/App");
        app.SetCookie(SignInRoles.CookieName).Should().StartWith($"{SignInRoles.CookieName}=tester");
        var html = await app.Content.ReadAsStringAsync();
        ShowsTesterMenuOnly(html);
        Toggle(html).Should().Contain("aria-current=\"true\">Tester</span>", "the tester half is lit")
            .And.Contain("href=\"/Admin\" aria-label=\"Switch to Super Admin\"");
        html.Should().Contain("id=\"app-status-root\"");

        // The installed app opens "/" every time it starts: straight back to the tester side.
        (await browser.GetAsync("/")).Location().Should().Be("/App");
        ShowsTesterMenuOnly(await PageAsync(browser, "/Help"));
    }

    [Fact]
    public async Task Switching_sides_is_a_link_and_the_menu_follows()
    {
        var browser = await SignedInAsync([Roles.Tester, Roles.SuperAdministrator]);
        await PageAsync(browser, "/App");

        var admin = await PageAsync(browser, "/Admin");
        ShowsAdminMenuOnly(admin);
        Toggle(admin).Should().Contain("aria-current=\"true\">Admin</span>", "the admin half is lit")
            .And.Contain("href=\"/App\" aria-label=\"Switch to Tester\"");
        admin.Should().NotContain("id=\"app-status-root\"", "the connection status is the tester app's");
        (await browser.GetAsync("/")).Location().Should().Be("/Admin");
        ShowsAdminMenuOnly(await PageAsync(browser, "/Help"));
    }

    [Fact]
    public async Task Signing_in_again_asks_again()
    {
        var companyId = await CompanyAsync();
        var email = Email("again");
        await _factory.CreateUserAsync(email, Password, [Roles.Tester, Roles.CompanyAdministrator],
            configure: u => u.TestingCompanyId = companyId);
        var browser = _factory.CreateBrowser();
        await browser.LoginAsync(email, Password);
        await PageAsync(browser, "/App");
        (await browser.GetAsync("/")).Location().Should().Be("/App");

        (await browser.LogoutAsync()).Location().Should().Be("/Account/Login");
        (await browser.LoginAsync(email, Password)).Location().Should().Be("/");
        (await browser.GetAsync("/")).Location().Should().Be("/Account/ChooseRole");
    }

    [Fact]
    public async Task A_company_admin_who_tests_is_offered_the_company_admin_side()
    {
        var browser = await SignedInAsync([Roles.Tester, Roles.CompanyAdministrator]);

        var html = await PageAsync(browser, "/Account/ChooseRole");
        html.Should().Contain("You're a Tester and a Company Administrator.")
            .And.Contain("Review your company's tests and farms, and approve new farms.")
            .And.NotContain("Super Administrator");
        Toggle(await PageAsync(browser, "/App")).Should().Contain("aria-label=\"Switch to Company Admin\"");
    }

    [Fact]
    public async Task The_licence_reminder_shows_on_the_tester_side_only()
    {
        var expiry = NzTime.Today.AddDays(10);
        var browser = await SignedInAsync([Roles.Tester, Roles.SuperAdministrator],
            u => u.LicenceExpiryDate = expiry);

        (await PageAsync(browser, "/App")).Should().Contain($"Your Tester licence expires on {expiry:dd MMM yyyy}");
        (await PageAsync(browser, "/Admin")).Should().NotContain("Your Tester licence");
    }

    [Theory]
    [InlineData(Roles.Tester, "/App")]
    [InlineData(Roles.SuperAdministrator, "/Admin")]
    [InlineData(Roles.CompanyAdministrator, "/Admin")]
    public async Task One_role_users_are_never_asked(string role, string home)
    {
        var browser = await SignedInAsync([role]);

        (await browser.GetAsync("/")).Location().Should().Be(home);
        (await browser.GetAsync("/Account/ChooseRole")).Location().Should().Be("/");
        var html = await PageAsync(browser, home);
        html.Should().NotContain("data-role-switch");
        if (role == Roles.Tester) ShowsTesterMenuOnly(html); else ShowsAdminMenuOnly(html);
    }

    [Fact]
    public void Pages_belong_to_the_side_their_path_says()
    {
        SignInRoles.ForPath("/App/Tests/Wizard").Should().Be(SignInRole.Tester);
        SignInRoles.ForPath("/app").Should().Be(SignInRole.Tester);
        SignInRoles.ForPath("/Admin/Farms/Edit/1").Should().Be(SignInRole.Admin);
        SignInRoles.ForPath("/Help").Should().BeNull();
        SignInRoles.ForPath("/Applications").Should().BeNull("a segment match, not a prefix");
        SignInRoles.ForPath("/").Should().BeNull();
    }
}
