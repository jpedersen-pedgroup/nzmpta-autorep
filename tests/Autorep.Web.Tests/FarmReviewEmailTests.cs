using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Autorep.Web.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Autorep.Web.Tests;

// The one email about a farm still under review a week after a tester set it up in the field (Josh,
// 12 Nov 2026). Adding the farm emails nobody (NewFarmTests, SyncControllerTests); the admin portal
// shows it, with the 48-hour banner (FarmReviewReminderTests). The email goes to the banner's people,
// skips anyone who dismissed the banner, and sends each administrator one email for all their farms.
public class FarmReviewEmailTests : IClassFixture<AuthedWebAppFactory>
{
    private readonly AuthedWebAppFactory _factory;
    public FarmReviewEmailTests(AuthedWebAppFactory factory) => _factory = factory;

    // 10 pm on 12 Nov in New Zealand.
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    private static AutorepDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AutorepDbContext>().UseInMemoryDatabase("review-emails-" + Guid.NewGuid()).Options);

    private static FarmReviewEmails Emails(AutorepDbContext db, CapturingEmailSender sender) =>
        new(db, sender,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["App:PublicUrl"] = "https://autorep.test/" }).Build(),
            NullLogger<FarmReviewEmails>.Instance);

    private static Farm Pending(string name, Guid? company, TimeSpan waiting) =>
        new() { Name = name, CreatedByTestingCompanyId = company, PendingReviewSince = Now - waiting };

    private static readonly TimeSpan Week = TimeSpan.FromDays(7);
    private static readonly TimeSpan Overdue = TimeSpan.FromDays(8);

    private static async Task<Tester> SeedUserAsync(
        AutorepDbContext db, string id, Guid? companyId, string[] roles, string? email = "", string displayName = "")
    {
        var user = new Tester
        {
            Id = id, UserName = id, Email = email == "" ? $"{id}@example.test" : email,
            DisplayName = displayName, TestingCompanyId = companyId,
        };
        db.Users.Add(user);
        foreach (var name in roles)
        {
            var role = db.Roles.Local.FirstOrDefault(r => r.Name == name) ?? await db.Roles.FirstOrDefaultAsync(r => r.Name == name);
            if (role is null)
            {
                role = new IdentityRole { Name = name, NormalizedName = name.ToUpperInvariant() };
                db.Roles.Add(role);
            }
            db.UserRoles.Add(new IdentityUserRole<string> { UserId = id, RoleId = role.Id });
        }
        await db.SaveChangesAsync();
        return user;
    }

    private static Task<Tester> SeedCompanyAdminAsync(AutorepDbContext db, string id, Guid companyId, string displayName = "") =>
        SeedUserAsync(db, id, companyId, [Roles.CompanyAdministrator], displayName: displayName);

    private static async Task<TestingCompany> SeedCompanyAsync(AutorepDbContext db, string name)
    {
        var company = new TestingCompany { Name = name };
        db.TestingCompanies.Add(company);
        await db.SaveChangesAsync();
        return company;
    }

    // ---- When ---------------------------------------------------------------------------------------

    [Fact]
    public async Task The_reviewers_are_emailed_once_when_a_farm_has_waited_a_week_and_never_again()
    {
        using var db = NewDb();
        var company = await SeedCompanyAsync(db, "Co");
        await SeedCompanyAdminAsync(db, "ca", company.Id, displayName: "Cara Admin");
        await SeedUserAsync(db, "field", company.Id, [Roles.Tester], displayName: "Fern Fieldtester");
        var farm = Pending("Smith & Sons <Ltd>", company.Id, Week - TimeSpan.FromHours(1));
        farm.CreatedByTesterId = "field";
        db.Farms.Add(farm);
        await db.SaveChangesAsync();
        var sender = new CapturingEmailSender();

        (await Emails(db, sender).SendDueAsync(Now)).Should().Be(0, "six days and 23 hours isn't a week");
        farm.ReviewEmailSentAt.Should().BeNull();

        var aWeekOn = Now + TimeSpan.FromHours(1);
        (await Emails(db, sender).SendDueAsync(aWeekOn)).Should().Be(1);

        var mail = sender.All.Should().ContainSingle().Which;
        mail.Email.Should().Be("ca@example.test");
        mail.Subject.Should().Be("Farm still awaiting review: Smith & Sons <Ltd>");
        mail.HtmlMessage.Should().Contain("<p>Hi Cara Admin,</p>")
            .And.Contain($"<a href=\"https://autorep.test/Admin/Farms/Edit/{farm.Id}\">Smith &amp; Sons &lt;Ltd&gt;</a>, added by Fern Fieldtester on 5 Nov 2026")
            .And.Contain("only email");
        (await db.Farms.SingleAsync()).ReviewEmailSentAt.Should().Be(aWeekOn);

        (await Emails(db, sender).SendDueAsync(aWeekOn + TimeSpan.FromHours(1))).Should().Be(0);
        (await Emails(db, sender).SendDueAsync(aWeekOn + TimeSpan.FromDays(30))).Should().Be(0);
        sender.All.Should().ContainSingle("there's one email per review, not a weekly nag");
    }

    [Fact]
    public async Task A_farm_put_back_under_review_is_emailed_about_again()
    {
        using var db = NewDb();
        var company = await SeedCompanyAsync(db, "Co");
        await SeedCompanyAdminAsync(db, "ca", company.Id);
        var farm = Pending("Reviewed twice", company.Id, Overdue);
        farm.ReviewEmailSentAt = Now - TimeSpan.FromDays(40); // the earlier review's
        db.Farms.Add(farm);
        await db.SaveChangesAsync();
        var sender = new CapturingEmailSender();

        (await Emails(db, sender).SendDueAsync(Now)).Should().Be(1);
        sender.All.Should().ContainSingle().Which.Subject.Should().Contain("Reviewed twice");
    }

    // The migration stamps farms already under review with their own PendingReviewSince: the old code
    // emailed their reviewers the moment they were added, so the deploy mustn't send a burst.
    [Fact]
    public async Task Approved_farms_and_farms_already_emailed_this_review_are_left_alone()
    {
        using var db = NewDb();
        var company = await SeedCompanyAsync(db, "Co");
        await SeedCompanyAdminAsync(db, "ca", company.Id);
        var emailedWhenAdded = Pending("Emailed when added", company.Id, TimeSpan.FromDays(30));
        emailedWhenAdded.ReviewEmailSentAt = emailedWhenAdded.PendingReviewSince;
        db.Farms.AddRange(
            emailedWhenAdded,
            new Farm { Name = "Approved", CreatedByTestingCompanyId = company.Id, ReviewEmailSentAt = Now - TimeSpan.FromDays(60) },
            new Farm { Name = "Never reviewed", CreatedByTestingCompanyId = company.Id });
        await db.SaveChangesAsync();
        var sender = new CapturingEmailSender();

        (await Emails(db, sender).SendDueAsync(Now)).Should().Be(0);
        sender.All.Should().BeEmpty();
    }

    // ---- Who ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Each_administrator_gets_one_email_listing_all_their_farms()
    {
        using var db = NewDb();
        var mine = await SeedCompanyAsync(db, "Mine");
        var theirs = await SeedCompanyAsync(db, "Theirs");
        await SeedCompanyAdminAsync(db, "ca-1", mine.Id);
        await SeedCompanyAdminAsync(db, "ca-2", mine.Id);
        await SeedCompanyAdminAsync(db, "other-ca", theirs.Id);
        await SeedUserAsync(db, "tester", mine.Id, [Roles.Tester]);
        var a = Pending("Farm A", mine.Id, Overdue);
        var b = Pending("Farm B", mine.Id, TimeSpan.FromDays(9));
        var c = Pending("Farm C", mine.Id, TimeSpan.FromDays(10));
        var other = Pending("Their farm", theirs.Id, Overdue);
        db.Farms.AddRange(a, b, c, other);
        await db.SaveChangesAsync();
        var sender = new CapturingEmailSender();

        (await Emails(db, sender).SendDueAsync(Now)).Should().Be(3);

        sender.All.Select(m => m.Email).Should().BeEquivalentTo("ca-1@example.test", "ca-2@example.test", "other-ca@example.test");
        foreach (var mail in sender.All.Where(m => m.Email.StartsWith("ca-")))
        {
            mail.Subject.Should().Be("3 farms still awaiting review");
            mail.HtmlMessage.Should().Contain($"/Admin/Farms/Edit/{a.Id}").And.Contain($"/Admin/Farms/Edit/{b.Id}")
                .And.Contain($"/Admin/Farms/Edit/{c.Id}").And.NotContain(other.Id.ToString());
            mail.HtmlMessage.IndexOf("Farm C", StringComparison.Ordinal)
                .Should().BeLessThan(mail.HtmlMessage.IndexOf("Farm A", StringComparison.Ordinal), "oldest first");
        }
        var theirMail = sender.All.Single(m => m.Email == "other-ca@example.test");
        theirMail.Subject.Should().Be("Farm still awaiting review: Their farm");
        theirMail.HtmlMessage.Should().NotContain(a.Id.ToString());
    }

    [Fact]
    public async Task An_administrator_who_dismissed_the_banner_isnt_emailed()
    {
        using var db = NewDb();
        var company = await SeedCompanyAsync(db, "Co");
        await SeedCompanyAdminAsync(db, "dismissed-it", company.Id);
        await SeedCompanyAdminAsync(db, "dismissed-an-earlier-review", company.Id);
        await SeedCompanyAdminAsync(db, "never-dismissed", company.Id);
        var farm = Pending("Seen it", company.Id, Overdue);
        db.Farms.Add(farm);
        db.FarmReviewDismissals.AddRange(
            new FarmReviewDismissal { UserId = "dismissed-it", FarmId = farm.Id, DismissedAt = farm.PendingReviewSince!.Value.AddDays(3) },
            new FarmReviewDismissal { UserId = "dismissed-an-earlier-review", FarmId = farm.Id, DismissedAt = farm.PendingReviewSince!.Value.AddDays(-30) });
        await db.SaveChangesAsync();
        var sender = new CapturingEmailSender();

        (await Emails(db, sender).SendDueAsync(Now)).Should().Be(2);

        sender.All.Select(m => m.Email).Should().BeEquivalentTo(
            "dismissed-an-earlier-review@example.test", "never-dismissed@example.test");
        farm.ReviewEmailSentAt.Should().Be(Now);
    }

    [Fact]
    public async Task A_farm_with_no_company_goes_to_the_super_administrators()
    {
        using var db = NewDb();
        var company = await SeedCompanyAsync(db, "Co");
        await SeedUserAsync(db, "sa", null, [Roles.SuperAdministrator]);
        await SeedCompanyAdminAsync(db, "ca", company.Id);
        var orphan = Pending("Orphan", null, Overdue);
        var companys = Pending("A company's", company.Id, Overdue);
        db.Farms.AddRange(orphan, companys);
        await db.SaveChangesAsync();
        var sender = new CapturingEmailSender();

        (await Emails(db, sender).SendDueAsync(Now)).Should().Be(2);

        var toSuper = sender.All.Single(m => m.Email == "sa@example.test");
        toSuper.Subject.Should().Be("Farm still awaiting review: Orphan");
        toSuper.HtmlMessage.Should().Contain("needs an MPNZ administrator", "the Super Administrator is told why it reached them")
            .And.NotContain(companys.Id.ToString());
        sender.All.Single(m => m.Email == "ca@example.test").HtmlMessage.Should().NotContain(orphan.Id.ToString())
            .And.NotContain("MPNZ administrator");
    }

    [Fact]
    public async Task Deactivated_administrators_and_ones_without_an_email_address_are_left_out()
    {
        using var db = NewDb();
        var company = await SeedCompanyAsync(db, "Co");
        var deactivated = await SeedCompanyAdminAsync(db, "deactivated", company.Id);
        deactivated.LockoutEnd = AccountLockout.DeactivatedUntil();
        var lockedOutForAMoment = await SeedCompanyAdminAsync(db, "locked-out", company.Id);
        lockedOutForAMoment.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(15); // too many password attempts
        await SeedUserAsync(db, "no-email", company.Id, [Roles.CompanyAdministrator], email: null);
        await SeedUserAsync(db, "tester-only", company.Id, [Roles.Tester]);
        db.Farms.Add(Pending("Farm", company.Id, Overdue));
        await db.SaveChangesAsync();
        var sender = new CapturingEmailSender();

        (await Emails(db, sender).SendDueAsync(Now)).Should().Be(1);

        sender.All.Should().ContainSingle().Which.Email.Should().Be("locked-out@example.test");
    }

    // One unreachable mailbox mustn't swallow the email to the others. The farm stays stamped, so the
    // failure is logged rather than retried every hour.
    [Fact]
    public async Task A_failed_send_doesnt_stop_the_others()
    {
        using var db = NewDb();
        var company = await SeedCompanyAsync(db, "Co");
        await SeedCompanyAdminAsync(db, "broken", company.Id);
        await SeedCompanyAdminAsync(db, "ok", company.Id);
        db.Farms.Add(Pending("Farm", company.Id, Overdue));
        await db.SaveChangesAsync();
        var sender = new CapturingEmailSender { FailFor = e => e == "broken@example.test" };

        (await Emails(db, sender).SendDueAsync(Now)).Should().Be(1);
        (await Emails(db, sender).SendDueAsync(Now + TimeSpan.FromHours(1))).Should().Be(0);

        sender.All.Should().ContainSingle().Which.Email.Should().Be("ok@example.test");
        (await db.Farms.SingleAsync()).ReviewEmailSentAt.Should().Be(Now);
    }

    // ---- Wiring -------------------------------------------------------------------------------------

    [Theory]
    [InlineData("https://autorep.example.nz/", "app-x.azurewebsites.net", "https://autorep.example.nz")]
    [InlineData(null, "app-x.azurewebsites.net", "https://app-x.azurewebsites.net")]
    [InlineData(null, null, "")]
    public void Links_use_the_configured_address_then_the_App_Service_host(string? publicUrl, string? websiteHostname, string expected)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["App:PublicUrl"] = publicUrl,
            ["WEBSITE_HOSTNAME"] = websiteHostname,
        }).Build();

        FarmReviewEmails.PublicUrl(config).Should().Be(expected);
    }

    [Fact]
    public void The_app_runs_the_check_in_the_background()
    {
        _factory.Services.GetServices<IHostedService>().Should().ContainSingle(s => s is FarmReviewEmailService);
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<FarmReviewEmails>().Should().NotBeNull();
    }
}
