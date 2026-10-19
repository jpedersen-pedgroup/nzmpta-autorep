using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Autorep.Web.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Autorep.Web.Tests;

// The admin portal's reminder about farms under review for 48 hours (Josh, 19 Oct): shown to whoever
// reviews the farm (FarmReviewNotifier's recipients), dismissible per administrator per farm, gone
// once the farm is approved. The review itself, the farms-list alert and the home badge are unchanged.
public class FarmReviewReminderTests : IClassFixture<AuthedWebAppFactory>
{
    private readonly AuthedWebAppFactory _factory;
    public FarmReviewReminderTests(AuthedWebAppFactory factory) => _factory = factory;

    private static readonly DateTimeOffset Now = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    private static AutorepDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AutorepDbContext>().UseInMemoryDatabase("reminders-" + Guid.NewGuid()).Options);

    private static ClaimsPrincipal As(string userId, params string[] roles) =>
        new(new ClaimsIdentity(
            roles.Select(r => new Claim(ClaimTypes.Role, r)).Prepend(new Claim(ClaimTypes.NameIdentifier, userId)), "Test"));

    private static Farm Pending(string name, Guid? company, TimeSpan waiting) =>
        new() { Name = name, CreatedByTestingCompanyId = company, PendingReviewSince = Now - waiting };

    // ---- Who is reminded, and when -----------------------------------------------------------------

    [Fact]
    public async Task A_company_administrator_is_reminded_about_their_companys_farms_after_48_hours()
    {
        using var db = NewDb();
        var mine = new TestingCompany { Name = "Mine" };
        var theirs = new TestingCompany { Name = "Theirs" };
        db.TestingCompanies.AddRange(mine, theirs);
        db.Users.Add(new Tester { Id = "ca", UserName = "ca", TestingCompanyId = mine.Id });
        db.Users.Add(new Tester { Id = "field", UserName = "field@x", DisplayName = "Fern Fieldtester", TestingCompanyId = mine.Id });
        var overdue = Pending("Overdue", mine.Id, TimeSpan.FromHours(49));
        overdue.CreatedByTesterId = "field";
        db.Farms.AddRange(
            overdue,
            Pending("Not yet", mine.Id, TimeSpan.FromHours(47)),
            Pending("Other company's", theirs.Id, TimeSpan.FromDays(5)),
            Pending("Company-less", null, TimeSpan.FromDays(5)),
            new Farm { Name = "Approved", CreatedByTestingCompanyId = mine.Id });
        await db.SaveChangesAsync();

        var due = await FarmReviewReminders.DueForAsync(db, As("ca", Roles.CompanyAdministrator), Now);

        due.Should().ContainSingle().Which.Should().Be(
            new FarmReviewReminders.Item(overdue.Id, "Overdue", "Fern Fieldtester", overdue.PendingReviewSince!.Value));
    }

    [Fact]
    public async Task Super_administrators_are_reminded_about_company_less_farms_only()
    {
        using var db = NewDb();
        var company = new TestingCompany { Name = "Co" };
        db.TestingCompanies.Add(company);
        var orphan = Pending("Orphan", null, TimeSpan.FromDays(3));
        db.Farms.AddRange(orphan, Pending("A company's", company.Id, TimeSpan.FromDays(3)));
        await db.SaveChangesAsync();

        var due = await FarmReviewReminders.DueForAsync(db, As("sa", Roles.SuperAdministrator), Now);

        due.Select(i => i.FarmId).Should().Equal(orphan.Id);
        (await FarmReviewReminders.DueForAsync(db, As("t", Roles.Tester), Now)).Should().BeEmpty("testers aren't reviewers");
    }

    [Fact]
    public async Task Dismissing_hides_a_farm_for_that_administrator_only()
    {
        using var db = NewDb();
        var company = new TestingCompany { Name = "Co" };
        db.TestingCompanies.Add(company);
        db.Users.AddRange(
            new Tester { Id = "ca-1", UserName = "ca-1", TestingCompanyId = company.Id },
            new Tester { Id = "ca-2", UserName = "ca-2", TestingCompanyId = company.Id });
        var a = Pending("A", company.Id, TimeSpan.FromDays(3));
        var b = Pending("B", company.Id, TimeSpan.FromDays(4));
        db.Farms.AddRange(a, b);
        await db.SaveChangesAsync();

        (await FarmReviewReminders.DismissAsync(db, As("ca-1", Roles.CompanyAdministrator), [a.Id], Now)).Should().Be(1);

        (await FarmReviewReminders.DueForAsync(db, As("ca-1", Roles.CompanyAdministrator), Now))
            .Select(i => i.FarmId).Should().Equal(b.Id);
        (await FarmReviewReminders.DueForAsync(db, As("ca-2", Roles.CompanyAdministrator), Now))
            .Select(i => i.FarmId).Should().Equal(b.Id, a.Id); // oldest first
    }

    [Fact]
    public async Task A_dismissal_from_an_earlier_review_doesnt_hide_a_later_one()
    {
        using var db = NewDb();
        var company = new TestingCompany { Name = "Co" };
        db.TestingCompanies.Add(company);
        db.Users.Add(new Tester { Id = "ca", UserName = "ca", TestingCompanyId = company.Id });
        var farm = Pending("Reviewed twice", company.Id, TimeSpan.FromDays(3));
        db.Farms.Add(farm);
        db.FarmReviewDismissals.Add(new FarmReviewDismissal { UserId = "ca", FarmId = farm.Id, DismissedAt = farm.PendingReviewSince!.Value.AddDays(-30) });
        await db.SaveChangesAsync();

        (await FarmReviewReminders.DueForAsync(db, As("ca", Roles.CompanyAdministrator), Now)).Should().ContainSingle();

        (await FarmReviewReminders.DismissAsync(db, As("ca", Roles.CompanyAdministrator), [farm.Id], Now)).Should().Be(1);
        (await db.FarmReviewDismissals.CountAsync()).Should().Be(1, "the earlier row is moved on, not duplicated");
        (await FarmReviewReminders.DueForAsync(db, As("ca", Roles.CompanyAdministrator), Now)).Should().BeEmpty();
    }

    [Fact]
    public async Task Dismiss_ignores_farms_the_administrator_isnt_reminded_about()
    {
        using var db = NewDb();
        var mine = new TestingCompany { Name = "Mine" };
        var theirs = new TestingCompany { Name = "Theirs" };
        db.TestingCompanies.AddRange(mine, theirs);
        db.Users.Add(new Tester { Id = "ca", UserName = "ca", TestingCompanyId = mine.Id });
        var foreign = Pending("Foreign", theirs.Id, TimeSpan.FromDays(3));
        var fresh = Pending("Fresh", mine.Id, TimeSpan.FromHours(1));
        db.Farms.AddRange(foreign, fresh);
        await db.SaveChangesAsync();

        (await FarmReviewReminders.DismissAsync(db, As("ca", Roles.CompanyAdministrator), [foreign.Id, fresh.Id, Guid.NewGuid()], Now))
            .Should().Be(0);
        (await db.FarmReviewDismissals.AnyAsync()).Should().BeFalse();
    }

    // ---- Through the admin pages ------------------------------------------------------------------

    [Fact]
    public async Task The_banner_shows_on_admin_pages_and_dismiss_returns_to_the_page_without_it()
    {
        var (adminId, farm) = await SeedOverdueFarmAsync();
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, Roles.CompanyAdministrator);
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, adminId);

        var page = await client.GetAsync("/Admin/Tests?status=complete");
        var html = await page.Content.ReadAsStringAsync();
        var banner = BannerOf(html);
        banner.Should().Contain($"/Admin/Farms/Edit/{farm.Id}").And.Contain("more than 48 hours");
        WebUtility.HtmlDecode(banner).Should().Contain(farm.Name);

        // Post the banner's own Dismiss form, as the browser would.
        var token = Regex.Match(banner, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        var cookie = page.Headers.GetValues("Set-Cookie").First(c => c.StartsWith(".AspNetCore.Antiforgery")).Split(';')[0];
        var action = WebUtility.HtmlDecode(Regex.Match(banner, "<form[^>]*action=\"([^\"]+)\"").Groups[1].Value);
        action.Should().Be("/Admin/Farms?handler=DismissReview");
        var post = new HttpRequestMessage(HttpMethod.Post, action)
        {
            Content = new FormUrlEncodedContent(
            [
                new("farmIds", farm.Id.ToString()),
                new("returnUrl", "/Admin/Tests?status=complete"),
                new("__RequestVerificationToken", token),
            ]),
        };
        post.Headers.Add("Cookie", cookie);
        var res = await client.SendAsync(post);

        res.StatusCode.Should().Be(HttpStatusCode.Redirect);
        res.Headers.Location!.ToString().Should().Be("/Admin/Tests?status=complete");
        BannerOf(await client.GetStringAsync("/Admin/Tests")).Should().BeEmpty();

        // The farms list's own alert and the home tile badge carry on regardless.
        (await client.GetStringAsync("/Admin/Farms")).Should().Contain("awaiting review");
        (await client.GetStringAsync("/Admin")).Should().Contain("awaiting review");
    }

    [Fact]
    public async Task Dismiss_wont_redirect_off_site()
    {
        var (adminId, farm) = await SeedOverdueFarmAsync();
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, Roles.CompanyAdministrator);
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, adminId);
        var page = await client.GetAsync("/Admin/Farms");
        var html = await page.Content.ReadAsStringAsync();
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        var cookie = page.Headers.GetValues("Set-Cookie").First(c => c.StartsWith(".AspNetCore.Antiforgery")).Split(';')[0];

        var post = new HttpRequestMessage(HttpMethod.Post, "/Admin/Farms?handler=DismissReview")
        {
            Content = new FormUrlEncodedContent(
            [
                new("farmIds", farm.Id.ToString()),
                new("returnUrl", "https://evil.example/"),
                new("__RequestVerificationToken", token),
            ]),
        };
        post.Headers.Add("Cookie", cookie);
        var res = await client.SendAsync(post);

        res.Headers.Location!.ToString().Should().Be("/Admin/Farms");
    }

    private static string BannerOf(string html) =>
        Regex.Match(html, "<div class=\"alert alert--warning\" role=\"region\" aria-label=\"Farms waiting for review\" data-farm-review-banner>(.*?)</div>",
            RegexOptions.Singleline).Groups[1].Value;

    private async Task<(string AdminId, Farm Farm)> SeedOverdueFarmAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
        var company = new TestingCompany { Name = "Reminder Co " + Guid.NewGuid() };
        db.TestingCompanies.Add(company);
        var adminId = "reminder-admin-" + Guid.NewGuid().ToString("N");
        db.Users.Add(new Tester { Id = adminId, UserName = adminId, TestingCompanyId = company.Id });
        var farm = new Farm
        {
            Name = "Kōwhai & Co's farm " + Guid.NewGuid().ToString("N")[..6],
            CreatedByTestingCompanyId = company.Id,
            PendingReviewSince = DateTimeOffset.UtcNow.AddDays(-3),
        };
        db.Farms.Add(farm);
        await db.SaveChangesAsync();
        return (adminId, farm);
    }
}
