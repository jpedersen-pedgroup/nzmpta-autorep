using System.Security.Claims;
using Autorep.Web.Api;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Autorep.Web.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Autorep.Web.Tests;

// A tester adding a farm from the New-test page (POST /api/farms). This was a Razor handler on
// that page until the page moved to the device (Client/ui/NewTestApp.tsx); the rules carried over
// unchanged. Controller-level so each case gets its own store and its own mail capture.
//
// Starting a test is no longer a server round trip, so the old OnPost scope checks have no handler
// to test. What they protected is covered where data now crosses: the farm book only lists active,
// in-scope farms (FarmsControllerTests.List_*), GET /api/farms/{id} 404s outside scope, and the sync
// push links within scope (SyncControllerTests).
public class NewFarmTests
{
    private static AutorepDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AutorepDbContext>()
            .UseInMemoryDatabase("newfarm-" + Guid.NewGuid())
            .Options);

    private static FarmsController Controller(AutorepDbContext db,
        CapturingEmailSender? emails = null, string[]? extraRoles = null, IEnumerable<Claim>? extraClaims = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "tester-1"), new(ClaimTypes.Role, Roles.Tester) };
        claims.AddRange((extraRoles ?? []).Select(r => new Claim(ClaimTypes.Role, r)));
        claims.AddRange(extraClaims ?? []);
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        var notifier = new FarmReviewNotifier(db, emails ?? new CapturingEmailSender(),
            NullLogger<FarmReviewNotifier>.Instance);
        var http = new DefaultHttpContext { User = user };
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("autorep.test");
        return new FarmsController(db, notifier) { ControllerContext = new ControllerContext { HttpContext = http } };
    }

    private static FarmsController.NewFarmRequest Named(string name, Guid? regionId = null, Guid? milkId = null) =>
        new(name, null, milkId, regionId, null, null, null, null, null, null, null, null);

    // Seeds a Company Administrator (with role rows, so the notifier can find them) in the
    // given company.
    private static async Task SeedCompanyAdminAsync(AutorepDbContext db, Guid companyId, string email)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == Roles.CompanyAdministrator);
        if (role is null)
        {
            role = new IdentityRole
            {
                Name = Roles.CompanyAdministrator,
                NormalizedName = Roles.CompanyAdministrator.ToUpperInvariant(),
            };
            db.Roles.Add(role);
        }
        var admin = new Tester
        {
            Id = "admin-" + Guid.NewGuid(), UserName = email, Email = email,
            DisplayName = "Admin", TestingCompanyId = companyId,
        };
        db.Users.Add(admin);
        db.UserRoles.Add(new IdentityUserRole<string> { RoleId = role.Id, UserId = admin.Id });
        await db.SaveChangesAsync();
    }

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

    // A plain Tester's field-created farm goes under review, and the company's administrators
    // are emailed to look at it. The farm itself stays immediately usable.
    [Fact]
    public async Task A_testers_farm_is_flagged_for_review_and_notifies_the_company_admins()
    {
        using var db = NewDb();
        var companyId = await SeedTesterCompanyAsync(db);
        await SeedCompanyAdminAsync(db, companyId, "admin@testco.example");
        var emails = new CapturingEmailSender();

        var result = await Controller(db, emails).Create(Named("Field farm"), default);

        var dto = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<FarmsController.FarmDto>().Subject;
        dto.Name.Should().Be("Field farm");
        var farm = await db.Farms.SingleAsync(f => f.Name == "Field farm");
        dto.Id.Should().Be(farm.Id, "the device adds exactly this row to its cached farm book");
        farm.PendingReviewSince.Should().NotBeNull();
        farm.CreatedByTesterId.Should().Be("tester-1");
        farm.CreatedByTestingCompanyId.Should().Be(companyId);
        farm.IsActive.Should().BeTrue("a pending farm must stay usable for testing");

        var mail = emails.All.Should().ContainSingle().Which;
        mail.Email.Should().Be("admin@testco.example");
        mail.Subject.Should().Contain("Field farm");
        mail.HtmlMessage.Should().Contain($"https://autorep.test/Admin/Farms/Edit/{farm.Id}");
    }

    // One unreachable admin mailbox must not swallow the notification to the others — the
    // send is guarded per recipient, not once around the whole loop.
    [Fact]
    public async Task Still_notifies_the_other_admins_when_one_send_fails()
    {
        using var db = NewDb();
        var companyId = await SeedTesterCompanyAsync(db);
        await SeedCompanyAdminAsync(db, companyId, "broken@testco.example");
        await SeedCompanyAdminAsync(db, companyId, "ok@testco.example");
        var emails = new CapturingEmailSender { FailFor = e => e == "broken@testco.example" };

        await Controller(db, emails).Create(Named("Two admin farm"), default);

        emails.All.Should().ContainSingle().Which.Email.Should().Be("ok@testco.example");
        (await db.Farms.SingleAsync(f => f.Name == "Two admin farm"))
            .PendingReviewSince.Should().NotBeNull("the flag must stand even when mail fails");
    }

    // A user who also holds an administrator role doesn't need a second pair of eyes: their
    // farm is not flagged and no notification goes out.
    [Fact]
    public async Task A_company_administrators_farm_is_not_flagged_and_sends_no_mail()
    {
        using var db = NewDb();
        var companyId = await SeedTesterCompanyAsync(db);
        await SeedCompanyAdminAsync(db, companyId, "admin@testco.example");
        var emails = new CapturingEmailSender();

        var result = await Controller(db, emails, [Roles.CompanyAdministrator]).Create(Named("Admin farm"), default);

        result.Should().BeOfType<OkObjectResult>();
        (await db.Farms.SingleAsync(f => f.Name == "Admin farm")).PendingReviewSince.Should().BeNull();
        emails.All.Should().BeEmpty();
    }

    // A lapsed licence can't start tests, so it has no business adding farms either.
    [Fact]
    public async Task A_sync_only_session_cannot_add_farms()
    {
        using var db = NewDb();
        await SeedTesterCompanyAsync(db);

        var result = await Controller(db, extraClaims: [new Claim(LicenceScope.ScopeClaim, LicenceScope.SyncOnly)])
            .Create(Named("Lapsed farm"), default);

        result.Should().BeOfType<ForbidResult>();
        (await db.Farms.CountAsync()).Should().Be(0);
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
