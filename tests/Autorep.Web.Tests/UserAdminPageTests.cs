using System.Net;
using System.Text.RegularExpressions;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Autorep.Web.Tests;

// The Super Administrator's Users pages (/Admin/Testers): who must belong to a Testing Company.
// A Tester works for one and a Company Administrator runs one; a Super Administrator needs none.
public class UserAdminPageTests : IClassFixture<AuthedWebAppFactory>
{
    private readonly AuthedWebAppFactory _factory;
    public UserAdminPageTests(AuthedWebAppFactory factory) => _factory = factory;

    [Theory]
    [InlineData(new[] { Roles.Tester }, "Testers must be assigned to a Testing Company.")]
    [InlineData(new[] { Roles.CompanyAdministrator }, "Company Administrators must be assigned to a Testing Company.")]
    [InlineData(new[] { Roles.CompanyAdministrator, Roles.Tester }, "Testers and Company Administrators must be assigned to a Testing Company.")]
    [InlineData(new[] { Roles.SuperAdministrator }, null)]
    [InlineData(new[] { Roles.SuperAdministrator, Roles.CompanyAdministrator }, "Company Administrators must be assigned to a Testing Company.")]
    public void Company_required_error_names_the_roles_that_need_one(string[] roles, string? expected)
    {
        Roles.CompanyRequiredError(roles, testingCompanyId: null).Should().Be(expected);
        Roles.CompanyRequiredError(roles, Guid.NewGuid()).Should().BeNull();
    }

    [Fact]
    public async Task New_company_administrator_without_a_company_is_refused()
    {
        var email = $"ca-{Guid.NewGuid():N}@test.local";
        var res = await PostNewUserAsync(email, Roles.CompanyAdministrator, companyId: null);

        res.StatusCode.Should().Be(HttpStatusCode.OK, "the form comes back with the error");
        WebUtility.HtmlDecode(await res.Content.ReadAsStringAsync())
            .Should().Contain("Company Administrators must be assigned to a Testing Company.");
        (await FindUserAsync(email)).Should().BeNull();
    }

    [Fact]
    public async Task New_company_administrator_with_a_company_is_created()
    {
        var companyId = await SeedCompanyAsync();
        var email = $"ca-{Guid.NewGuid():N}@test.local";
        var res = await PostNewUserAsync(email, Roles.CompanyAdministrator, companyId);

        res.StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await FindUserAsync(email))!.TestingCompanyId.Should().Be(companyId);
    }

    [Fact]
    public async Task New_super_administrator_can_be_left_without_a_company()
    {
        var email = $"sa-{Guid.NewGuid():N}@test.local";
        var res = await PostNewUserAsync(email, Roles.SuperAdministrator, companyId: null);

        res.StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await FindUserAsync(email))!.TestingCompanyId.Should().BeNull();
    }

    // The company isn't part of Details any more: a stray field can't move or strip anyone.
    [Fact]
    public async Task Saving_details_leaves_the_company_alone()
    {
        var companyId = await SeedCompanyAsync();
        var userId = await SeedUserAsync(companyId, Roles.CompanyAdministrator);

        var res = await PostAsync($"/Admin/Testers/Edit/{userId}?handler=Details",
        [
            new("Details.DisplayName", "Still an admin"),
            new("Details.SelectedRoles", Roles.CompanyAdministrator),
            new("Details.TestingCompanyId", ""),
        ]);

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = (await FindUserByIdAsync(userId))!;
        saved.DisplayName.Should().Be("Still an admin");
        saved.TestingCompanyId.Should().Be(companyId);
    }

    [Fact]
    public async Task A_company_role_cant_be_added_until_the_user_has_a_company()
    {
        var userId = await SeedUserAsync(null, Roles.SuperAdministrator);

        var res = await PostAsync($"/Admin/Testers/Edit/{userId}?handler=Details",
        [
            new("Details.DisplayName", "Super"),
            new("Details.SelectedRoles", Roles.SuperAdministrator),
            new("Details.SelectedRoles", Roles.CompanyAdministrator),
        ]);

        WebUtility.HtmlDecode(await res.Content.ReadAsStringAsync())
            .Should().Contain("Company Administrators must be assigned to a Testing Company. Give them one with Change company first.");
        await using var scope = _factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Tester>>();
        (await users.GetRolesAsync((await users.FindByIdAsync(userId))!)).Should().Equal(Roles.SuperAdministrator);
    }

    // ---- Change company -------------------------------------------------------------------------

    [Fact]
    public async Task Edit_page_offers_change_company_and_says_what_happens()
    {
        var companyId = await SeedCompanyAsync();
        var userId = await SeedUserAsync(companyId, Roles.Tester);

        var html = WebUtility.HtmlDecode(await SuperAdminClient().GetStringAsync($"/Admin/Testers/Edit/{userId}"));

        html.Should().Contain("data-open-change-company").And.Contain("Change company");
        html.Should().Contain("The farms they've tested stay with");
        html.Should().NotContain("name=\"Details.TestingCompanyId\"", "the company is changed only through the dialog");
    }

    [Fact]
    public async Task Change_company_moves_a_tester_and_says_their_history_stays()
    {
        var from = await SeedCompanyAsync();
        var to = await SeedCompanyAsync();
        var userId = await SeedUserAsync(from, Roles.Tester);

        var res = await PostAsync($"/Admin/Testers/Edit/{userId}?handler=ChangeCompany", [new("NewCompanyId", to.ToString())]);

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        WebUtility.HtmlDecode(await res.Content.ReadAsStringAsync()).Should().Contain("Moved to ").And.Contain("stay with");
        (await FindUserByIdAsync(userId))!.TestingCompanyId.Should().Be(to);
    }

    [Fact]
    public async Task Change_company_wont_leave_a_tester_without_one()
    {
        var from = await SeedCompanyAsync();
        var userId = await SeedUserAsync(from, Roles.Tester);

        var res = await PostAsync($"/Admin/Testers/Edit/{userId}?handler=ChangeCompany", [new("NewCompanyId", "")]);

        WebUtility.HtmlDecode(await res.Content.ReadAsStringAsync()).Should().Contain("Testers must be assigned to a Testing Company.");
        (await FindUserByIdAsync(userId))!.TestingCompanyId.Should().Be(from);
    }

    [Fact]
    public async Task Change_company_refuses_a_deactivated_company()
    {
        var from = await SeedCompanyAsync();
        var closed = await SeedCompanyAsync(active: false);
        var userId = await SeedUserAsync(from, Roles.Tester);

        var res = await PostAsync($"/Admin/Testers/Edit/{userId}?handler=ChangeCompany", [new("NewCompanyId", closed.ToString())]);

        WebUtility.HtmlDecode(await res.Content.ReadAsStringAsync()).Should().Contain("Choose an active Testing Company.");
        (await FindUserByIdAsync(userId))!.TestingCompanyId.Should().Be(from);
    }

    [Fact]
    public async Task Change_company_can_set_and_clear_a_super_administrators_company()
    {
        var company = await SeedCompanyAsync();
        var userId = await SeedUserAsync(null, Roles.SuperAdministrator);

        await PostAsync($"/Admin/Testers/Edit/{userId}?handler=ChangeCompany", [new("NewCompanyId", company.ToString())]);
        (await FindUserByIdAsync(userId))!.TestingCompanyId.Should().Be(company);

        await PostAsync($"/Admin/Testers/Edit/{userId}?handler=ChangeCompany", [new("NewCompanyId", "")]);
        (await FindUserByIdAsync(userId))!.TestingCompanyId.Should().BeNull();
    }

    // Older or migrated accounts may break the rule; the list says who, without changing anything.
    [Fact]
    public async Task Users_list_names_company_administrators_left_without_a_company()
    {
        var orphanId = await SeedUserAsync(null, Roles.CompanyAdministrator);
        var superId = await SeedUserAsync(null, Roles.SuperAdministrator);
        var placedId = await SeedUserAsync(await SeedCompanyAsync(), Roles.CompanyAdministrator);

        var html = await SuperAdminClient().GetStringAsync("/Admin/Testers");
        var warning = Regex.Match(html, "<div class=\"alert alert--warning\" data-missing-company>(.*?)</div>", RegexOptions.Singleline).Groups[1].Value;

        warning.Should().Contain($"/Admin/Testers/Edit/{orphanId}").And.Contain("(Company Administrator)");
        warning.Should().NotContain(superId).And.NotContain(placedId);
        (await FindUserByIdAsync(orphanId))!.TestingCompanyId.Should().BeNull("the page only reports it");
    }

    [Fact]
    public async Task Users_pages_say_users_not_testers()
    {
        var client = SuperAdminClient();
        var list = WebUtility.HtmlDecode(await client.GetStringAsync("/Admin/Testers"));
        list.Should().Contain("<h1>Users</h1>").And.Contain("+ New User");

        var form = WebUtility.HtmlDecode(await client.GetStringAsync("/Admin/Testers/New"));
        form.Should().Contain("<h1>New user</h1>").And.Contain("Create User").And.NotContain("admins may be unassigned");
    }

    [Fact]
    public async Task Super_admin_nav_drops_regions_and_names_users_and_dairy_processors()
    {
        var html = await SuperAdminClient().GetStringAsync("/Admin/Testers");
        // The layout's scoped CSS adds a b-xxxx attribute to the tag.
        var nav = Regex.Match(html, "<nav[^>]*class=\"app-header__nav\"[^>]*>(.*?)</nav>", RegexOptions.Singleline).Groups[1].Value;

        nav.Should().NotBeEmpty("the page renders the site header").And.Contain(">Users</a>").And.Contain(">Dairy processors</a>");
        nav.Should().NotContain("/Admin/Regions", "Regions is still picked on farm forms, but has no nav link")
            .And.NotContain(">Testers</a>").And.NotContain("Milk companies");
        (await SuperAdminClient().GetAsync("/Admin/Regions")).StatusCode
            .Should().Be(HttpStatusCode.OK, "the page is still reachable by URL");
    }

    // --- helpers ---------------------------------------------------------------------------------

    private HttpClient SuperAdminClient()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, Roles.SuperAdministrator);
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "users-page-super");
        return client;
    }

    private Task<HttpResponseMessage> PostNewUserAsync(string email, string role, Guid? companyId) =>
        PostAsync("/Admin/Testers/New",
        [
            new("Input.Email", email),
            new("Input.DisplayName", "New Person"),
            new("Input.SelectedRoles", role),
            new("Input.TestingCompanyId", companyId?.ToString() ?? ""),
        ]);

    private async Task<HttpResponseMessage> PostAsync(string path, List<KeyValuePair<string, string>> fields)
    {
        await EnsureRolesAsync();
        var client = SuperAdminClient();
        var formPath = path.Split('?')[0];
        var page = await client.GetAsync(formPath);
        page.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await page.Content.ReadAsStringAsync();
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        var cookie = page.Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith(".AspNetCore.Antiforgery", StringComparison.Ordinal))
            .Split(';')[0];

        fields.Add(new("__RequestVerificationToken", token));
        var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = new FormUrlEncodedContent(fields) };
        req.Headers.Add("Cookie", cookie);
        return await client.SendAsync(req);
    }

    private async Task EnsureRolesAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in Roles.All)
            if (!await roles.RoleExistsAsync(role)) await roles.CreateAsync(new IdentityRole(role));
    }

    private async Task<Guid> SeedCompanyAsync(bool active = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
        var company = new TestingCompany { Name = "Users Co " + Guid.NewGuid().ToString("N")[..6], IsActive = active };
        db.TestingCompanies.Add(company);
        await db.SaveChangesAsync();
        return company.Id;
    }

    private async Task<string> SeedUserAsync(Guid? companyId, params string[] roles)
    {
        await EnsureRolesAsync();
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Tester>>();
        var email = $"seeded-{Guid.NewGuid():N}@test.local";
        var user = new Tester { UserName = email, Email = email, DisplayName = "Seeded", TestingCompanyId = companyId };
        (await users.CreateAsync(user)).Succeeded.Should().BeTrue();
        (await users.AddToRolesAsync(user, roles)).Succeeded.Should().BeTrue();
        return user.Id;
    }

    private async Task<Tester?> FindUserAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
        return await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Email == email);
    }

    private async Task<Tester?> FindUserByIdAsync(string id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
        return await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id);
    }
}
