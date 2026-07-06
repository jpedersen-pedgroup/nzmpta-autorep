using System.Security.Claims;
using Autorep.Web.Data;
using Autorep.Web.Domain.Entities;
using Autorep.Web.Pages.App.Tests;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Tests;

// Page-model tests for the tester new-test flow (avoids the antiforgery/HTTP round-trip).
public class NewTestPageTests
{
    private static AutorepDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AutorepDbContext>()
            .UseInMemoryDatabase("newtest-" + Guid.NewGuid())
            .Options);

    private static NewModel TesterModel(AutorepDbContext db)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, "tester-1") }, "Test"));
        return new NewModel(db)
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext { User = user } }
        };
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
