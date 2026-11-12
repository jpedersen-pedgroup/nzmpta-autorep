using System.Security.Claims;
using Autorep.Web.Api;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Tests;

// A tester adding a farm from the New-test page (POST /api/farms). This was a Razor handler on
// that page until the page moved to the device (Client/ui/NewTestApp.tsx); the rules carried over
// unchanged. Controller-level so each case gets its own store and its own mail capture.
//
// Starting a test is no longer a server round trip, so the old OnPost scope checks have no handler
// to test. What they protected is covered where data now crosses: the farm book only lists active,
// in-scope farms (FarmsControllerTests.List_*), GET /api/farms/{id} 404s outside scope, and the sync
// push links within scope (SyncControllerTests).
//
// Adding a farm emails nobody (Josh, 12 Nov 2026): the controller has no mail sender at all. The
// reviewers hear about it in the admin portal, and by email only if it's still under review a week
// later (FarmReviewEmailTests).
public class NewFarmTests
{
    private static AutorepDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AutorepDbContext>()
            .UseInMemoryDatabase("newfarm-" + Guid.NewGuid())
            .Options);

    private static FarmsController Controller(AutorepDbContext db, string[]? extraRoles = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "tester-1"), new(ClaimTypes.Role, Roles.Tester) };
        claims.AddRange((extraRoles ?? []).Select(r => new Claim(ClaimTypes.Role, r)));
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        var http = new DefaultHttpContext { User = user };
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("autorep.test");
        return new FarmsController(db) { ControllerContext = new ControllerContext { HttpContext = http } };
    }

    private static FarmsController.NewFarmRequest Named(string name, Guid? regionId = null, Guid? milkId = null, Guid? id = null) =>
        new(name, null, milkId, regionId, null, null, null, null, null, null, null, null, id);

    // Seeds "tester-1" (the Controller principal) as a member of a fresh Testing Company.
    private static async Task<Guid> SeedTesterCompanyAsync(AutorepDbContext db)
    {
        var company = new TestingCompany { Name = "Test Co" };
        db.TestingCompanies.Add(company);
        db.Users.Add(new Tester { Id = "tester-1", UserName = "tester-1", TestingCompanyId = company.Id });
        await db.SaveChangesAsync();
        return company.Id;
    }

    // A tester without a Testing Company can't own a farm, so the form must refuse instead of
    // stranding an orphan farm row the tester can never select (the pre-fix dead loop).
    [Fact]
    public async Task Refuses_a_tester_without_a_testing_company()
    {
        using var db = NewDb(); // no Users row for "tester-1" → no company

        var result = await Controller(db).Create(Named("Orphan farm"), default);

        result.Should().BeOfType<BadRequestObjectResult>();
        (await db.Farms.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Requires_a_name()
    {
        using var db = NewDb();
        await SeedTesterCompanyAsync(db);

        var result = await Controller(db).Create(Named("   "), default);

        result.Should().BeOfType<BadRequestObjectResult>();
        (await db.Farms.CountAsync()).Should().Be(0);
    }

    // A plain Tester's field-created farm goes under review. The farm itself stays immediately usable.
    [Fact]
    public async Task A_testers_farm_is_flagged_for_review()
    {
        using var db = NewDb();
        var companyId = await SeedTesterCompanyAsync(db);

        var result = await Controller(db).Create(Named("Field farm"), default);

        var dto = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FarmsController.FarmDto>().Subject;
        dto.Name.Should().Be("Field farm");
        var farm = await db.Farms.SingleAsync(f => f.Name == "Field farm");
        dto.Id.Should().Be(farm.Id, "the device adds exactly this row to its cached farm book");
        farm.PendingReviewSince.Should().NotBeNull();
        farm.ReviewEmailSentAt.Should().BeNull("the reviewers are emailed only if it's still waiting a week later");
        farm.CreatedByTesterId.Should().Be("tester-1");
        farm.CreatedByTestingCompanyId.Should().Be(companyId);
        farm.IsActive.Should().BeTrue("a pending farm must stay usable for testing");
    }

    // A user who also holds an administrator role doesn't need a second pair of eyes: their
    // farm is not flagged.
    [Fact]
    public async Task A_company_administrators_farm_is_not_flagged()
    {
        using var db = NewDb();
        await SeedTesterCompanyAsync(db);

        var result = await Controller(db, [Roles.CompanyAdministrator]).Create(Named("Admin farm"), default);

        result.Should().BeOfType<OkObjectResult>();
        (await db.Farms.SingleAsync(f => f.Name == "Admin farm")).PendingReviewSince.Should().BeNull();
    }

    // Codex review of #75: the answer can be lost after the farm is saved, and the tester can't
    // tell. A retry with the same id must return that farm, not add a second one.
    [Fact]
    public async Task Adding_the_same_farm_again_returns_it_instead_of_a_duplicate()
    {
        using var db = NewDb();
        await SeedTesterCompanyAsync(db);
        var id = Guid.NewGuid();

        var first = await Controller(db).Create(Named("Retried farm", id: id), default);
        var second = await Controller(db).Create(Named("Retried farm", id: id), default);

        (await db.Farms.CountAsync(f => f.Name == "Retried farm")).Should().Be(1);
        var dto1 = first.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FarmsController.FarmDto>().Subject;
        var dto2 = second.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FarmsController.FarmDto>().Subject;
        dto1.Id.Should().Be(id);
        dto2.Id.Should().Be(id);
    }

    [Fact]
    public async Task An_id_that_belongs_to_someone_elses_farm_is_refused()
    {
        using var db = NewDb();
        await SeedTesterCompanyAsync(db);
        var theirs = new Farm { Name = "Not yours", CreatedByTesterId = "tester-2" };
        db.Farms.Add(theirs);
        await db.SaveChangesAsync();

        var result = await Controller(db).Create(Named("My farm", id: theirs.Id), default);

        result.Should().BeOfType<ConflictObjectResult>();
        (await db.Farms.SingleAsync(f => f.Id == theirs.Id)).Name.Should().Be("Not yours");
    }

    // The form only offers active rows; a stale form or crafted request gets a clear answer rather
    // than a foreign-key failure.
    [Fact]
    public async Task Rejects_a_region_or_milk_company_that_is_unknown_or_inactive()
    {
        using var db = NewDb();
        await SeedTesterCompanyAsync(db);
        var oldRegion = new Region { Name = "Retired region", Island = "North Island", IsActive = false };
        db.Regions.Add(oldRegion);
        await db.SaveChangesAsync();

        (await Controller(db).Create(Named("Farm A", regionId: oldRegion.Id), default))
            .Should().BeOfType<BadRequestObjectResult>();
        (await Controller(db).Create(Named("Farm B", milkId: Guid.NewGuid()), default))
            .Should().BeOfType<BadRequestObjectResult>();
        (await db.Farms.CountAsync()).Should().Be(0);
    }
}
