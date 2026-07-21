using System.Security.Claims;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Autorep.Web.Pages.App.Tests;
using Autorep.Web.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Autorep.Web.Tests;

// Page-model tests for the tester new-test flow (avoids the antiforgery/HTTP round-trip).
public class NewTestPageTests
{
    private static AutorepDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AutorepDbContext>()
            .UseInMemoryDatabase("newtest-" + Guid.NewGuid())
            .Options);

    private static NewModel TesterModel(AutorepDbContext db,
        CapturingEmailSender? emails = null, params string[] extraRoles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "tester-1") };
        claims.AddRange(extraRoles.Select(r => new Claim(ClaimTypes.Role, r)));
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        var notifier = new FarmReviewNotifier(db, emails ?? new CapturingEmailSender(),
            NullLogger<FarmReviewNotifier>.Instance);
        return new NewModel(db, notifier)
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext { User = user } }
        };
    }

    // Seeds a Company Administrator (with role rows, so the notifier can find them) in the
    // given company.
    private static async Task SeedCompanyAdminAsync(AutorepDbContext db, Guid companyId, string email)
    {
        var role = new IdentityRole
        {
            Name = Roles.CompanyAdministrator,
            NormalizedName = Roles.CompanyAdministrator.ToUpperInvariant(),
        };
        db.Roles.Add(role);
        var admin = new Tester
        {
            Id = "admin-" + Guid.NewGuid(), UserName = email, Email = email,
            DisplayName = "Admin", TestingCompanyId = companyId,
        };
        db.Users.Add(admin);
        db.UserRoles.Add(new IdentityUserRole<string> { RoleId = role.Id, UserId = admin.Id });
        await db.SaveChangesAsync();
    }

    // Seeds "tester-1" (the TesterModel principal) as a member of a fresh Testing Company,
    // returning the company id for tagging farms into (or out of) the tester's scope.
    private static async Task<Guid> SeedTesterCompanyAsync(AutorepDbContext db)
    {
        var company = new TestingCompany { Name = "Test Co" };
        db.TestingCompanies.Add(company);
        db.Users.Add(new Tester { Id = "tester-1", UserName = "tester-1", TestingCompanyId = company.Id });
        await db.SaveChangesAsync();
        return company.Id;
    }

    // Regression for PR #20: an inactive farm id (stale page / crafted POST) must be rejected.
    [Fact]
    public async Task OnPost_rejects_an_inactive_farm_and_creates_no_test()
    {
        using var db = NewDb();
        var companyId = await SeedTesterCompanyAsync(db);
        var farm = new Farm { Name = "Deactivated farm", IsActive = false, CreatedByTestingCompanyId = companyId };
        db.Farms.Add(farm);
        await db.SaveChangesAsync();

        var model = TesterModel(db);
        model.Input.FarmId = farm.Id;

        var result = await model.OnPostAsync();

        result.Should().BeOfType<PageResult>();
        model.Errors.Should().NotBeEmpty();
        (await db.MachineTests.CountAsync()).Should().Be(0);
    }

    // The picker's company scoping must hold server-side: a crafted POST with another
    // company's (or an unowned, untested) farm id must not start a test against it.
    [Fact]
    public async Task OnPost_rejects_an_active_farm_outside_the_testers_company_scope()
    {
        using var db = NewDb();
        await SeedTesterCompanyAsync(db);
        var otherCompany = new TestingCompany { Name = "Other Co" };
        db.TestingCompanies.Add(otherCompany);
        var foreignFarm = new Farm { Name = "Foreign farm", IsActive = true, CreatedByTestingCompanyId = otherCompany.Id };
        db.Farms.Add(foreignFarm);
        await db.SaveChangesAsync();

        var model = TesterModel(db);
        model.Input.FarmId = foreignFarm.Id;

        var result = await model.OnPostAsync();

        result.Should().BeOfType<PageResult>();
        model.Errors.Should().NotBeEmpty();
        (await db.MachineTests.CountAsync()).Should().Be(0);
    }

    // A tester without a Testing Company can't own a farm, so the modal must refuse instead of
    // stranding an orphan farm row the tester can never select (the pre-fix dead loop).
    [Fact]
    public async Task CreateFarm_refuses_a_tester_without_a_testing_company()
    {
        using var db = NewDb(); // no Users row for "tester-1" → no company
        var model = TesterModel(db);

        var result = await model.OnPostCreateFarmAsync(new NewModel.NewFarmModel { Name = "Orphan farm" });

        result.Should().BeOfType<BadRequestObjectResult>();
        (await db.Farms.CountAsync()).Should().Be(0);
    }

    // The migrated-farm shape: a farm with test history by the company but a null
    // CreatedByTestingCompanyId must be startable — the scope's tests leg, not just created-by.
    [Fact]
    public async Task OnPost_accepts_a_farm_in_scope_via_the_companys_test_history()
    {
        using var db = NewDb();
        var companyId = await SeedTesterCompanyAsync(db);
        var colleague = new Tester { Id = "tester-2", UserName = "tester-2", TestingCompanyId = companyId };
        db.Users.Add(colleague);
        var farm = new Farm { Name = "Legacy farm", IsActive = true }; // no CreatedBy — migrated shape
        db.Farms.Add(farm);
        db.MachineTests.Add(new MachineTest { TesterId = colleague.Id, FarmId = farm.Id });
        await db.SaveChangesAsync();

        var model = TesterModel(db);
        model.Input.FarmId = farm.Id;

        var result = await model.OnPostAsync();

        var redirect = result.Should().BeOfType<RedirectToPageResult>().Subject;
        redirect.RouteValues!["farmId"].Should().Be(farm.Id);
    }

    // A plain Tester's field-created farm goes under review, and the company's administrators
    // are emailed to look at it. The farm itself stays immediately usable.
    [Fact]
    public async Task CreateFarm_by_a_tester_is_flagged_for_review_and_notifies_the_company_admins()
    {
        using var db = NewDb();
        var companyId = await SeedTesterCompanyAsync(db);
        await SeedCompanyAdminAsync(db, companyId, "admin@testco.example");
        var emails = new CapturingEmailSender();

        var model = TesterModel(db, emails);
        var result = await model.OnPostCreateFarmAsync(new NewModel.NewFarmModel { Name = "Field farm" });

        result.Should().BeOfType<JsonResult>();
        var farm = await db.Farms.SingleAsync(f => f.Name == "Field farm");
        farm.PendingReviewSince.Should().NotBeNull();
        farm.CreatedByTesterId.Should().Be("tester-1");
        farm.IsActive.Should().BeTrue("a pending farm must stay usable for testing");

        var mail = emails.All.Should().ContainSingle().Which;
        mail.Email.Should().Be("admin@testco.example");
        mail.Subject.Should().Contain("Field farm");
    }

    // A user who also holds an administrator role doesn't need a second pair of eyes: their
    // farm is not flagged and no notification goes out.
    [Fact]
    public async Task CreateFarm_by_a_company_administrator_is_not_flagged_and_sends_no_mail()
    {
        using var db = NewDb();
        var companyId = await SeedTesterCompanyAsync(db);
        await SeedCompanyAdminAsync(db, companyId, "admin@testco.example");
        var emails = new CapturingEmailSender();

        var model = TesterModel(db, emails, Roles.CompanyAdministrator);
        var result = await model.OnPostCreateFarmAsync(new NewModel.NewFarmModel { Name = "Admin farm" });

        result.Should().BeOfType<JsonResult>();
        var farm = await db.Farms.SingleAsync(f => f.Name == "Admin farm");
        farm.PendingReviewSince.Should().BeNull();
        emails.All.Should().BeEmpty();
    }

    [Fact]
    public async Task OnPost_launches_the_wizard_for_an_active_farm_without_creating_a_server_test()
    {
        using var db = NewDb();
        var companyId = await SeedTesterCompanyAsync(db);
        var farm = new Farm { Name = "Active farm", IsActive = true, CreatedByTestingCompanyId = companyId };
        db.Farms.Add(farm);
        await db.SaveChangesAsync();

        var model = TesterModel(db);
        model.Input.FarmId = farm.Id;

        var result = await model.OnPostAsync();

        var redirect = result.Should().BeOfType<RedirectToPageResult>().Subject;
        redirect.PageName.Should().Be("/App/Tests/Wizard");
        redirect.RouteValues!["farmId"].Should().Be(farm.Id);
        // Offline-first: the Machine Test is created on-device in the wizard, not here.
        (await db.MachineTests.CountAsync()).Should().Be(0);
    }
}
