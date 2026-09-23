using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Autorep.Web.Pages.Admin.Tests;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Autorep.Web.Tests;

// The admin Upcoming tests page: each farm judged on its latest completed test, in the viewer's
// company scope, overdue farms always shown and the rest within the chosen window.
public class UpcomingTestsTests : IClassFixture<AuthedWebAppFactory>
{
    private readonly AuthedWebAppFactory _factory;
    public UpcomingTestsTests(AuthedWebAppFactory factory) => _factory = factory;

    private static readonly DateOnly Today = NzTime.Today;

    private static AutorepDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AutorepDbContext>().UseInMemoryDatabase("upcoming-" + Guid.NewGuid()).Options);

    private static UpcomingModel PageAs(AutorepDbContext db, string userId, string role)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId), new Claim(ClaimTypes.Role, role)], "Test"));
        var users = new UserManager<Tester>(
            new UserStore<Tester>(db), null!, null!, null!, null!, null!, null!, null!, null!);
        return new UpcomingModel(db, users)
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext { User = user } },
        };
    }

    private sealed class World
    {
        public required AutorepDbContext Db { get; init; }
        public Guid CompanyA { get; } = Guid.NewGuid();
        public Guid CompanyB { get; } = Guid.NewGuid();

        public Farm Farm(string name)
        {
            var f = new Farm { Name = name, SupplyNumber = "S-" + name, FarmerName = "Farmer " + name, ContactPhone = "021 000 000" };
            Db.Farms.Add(f);
            return f;
        }

        public MachineTest Test(Farm farm, Guid? company, int completedDaysAgo, int? dueInDays,
            string tester = "t1", Guid? clientId = null, Guid? supersedes = null, bool complete = true)
        {
            var t = new MachineTest
            {
                TesterId = tester,
                FarmId = farm.Id,
                TestingCompanyId = company,
                ClientId = clientId ?? Guid.NewGuid(),
                SupersedesClientId = supersedes,
                MarkedCompleteAt = complete ? DateTimeOffset.UtcNow.AddDays(-completedDaysAgo) : null,
                NextTestDate = dueInDays is { } d ? Today.AddDays(d) : null,
            };
            Db.MachineTests.Add(t);
            return t;
        }

        public static async Task<World> CreateAsync()
        {
            var w = new World { Db = NewDb() };
            w.Db.TestingCompanies.AddRange(
                new TestingCompany { Id = w.CompanyA, Name = "Company A" },
                new TestingCompany { Id = w.CompanyB, Name = "Company B" });
            w.Db.Users.AddRange(
                new Tester { Id = "admin-a", UserName = "admin-a", TestingCompanyId = w.CompanyA },
                new Tester { Id = "admin-none", UserName = "admin-none" },
                // Every test has a real tester: MachineTest.Tester is required, so the page's
                // query joins it, and a test pointing at a missing tester would drop out.
                new Tester { Id = "t1", UserName = "t1", DisplayName = "Tess Tester" });
            await w.Db.SaveChangesAsync();
            return w;
        }
    }

    [Fact]
    public async Task Lists_overdue_and_due_farms_in_date_order_and_leaves_later_ones_out()
    {
        var w = await World.CreateAsync();
        w.Test(w.Farm("Overdue"), w.CompanyA, 400, -20);
        w.Test(w.Farm("Soon"), w.CompanyA, 340, 10);
        w.Test(w.Farm("Later"), w.CompanyA, 300, 60);
        w.Test(w.Farm("Far"), w.CompanyA, 100, 250);
        await w.Db.SaveChangesAsync();

        var page = PageAs(w.Db, "admin-a", Roles.CompanyAdministrator);
        page.Window = 90;
        await page.OnGetAsync();

        page.Rows.Select(r => r.FarmName).Should().Equal("Overdue", "Soon", "Later");
        page.OverdueCount.Should().Be(1);
        page.DueSoonCount.Should().Be(1);

        page.Window = 0; // all upcoming
        await page.OnGetAsync();
        page.Rows.Should().HaveCount(4);
    }

    // The reason the page reads each farm's LATEST test: an old overdue date must not flag a farm
    // that has been tested since.
    [Fact]
    public async Task A_farm_tested_since_is_judged_on_its_newer_test()
    {
        var w = await World.CreateAsync();
        var farm = w.Farm("Retested");
        w.Test(farm, w.CompanyA, 500, -100);
        w.Test(farm, w.CompanyA, 20, 345);
        await w.Db.SaveChangesAsync();

        var page = PageAs(w.Db, "admin-a", Roles.CompanyAdministrator);
        await page.OnGetAsync();
        page.Rows.Should().BeEmpty("the newer test puts it 345 days out");

        page.Window = 0;
        await page.OnGetAsync();
        page.Rows.Should().ContainSingle().Which.NextTestDate.Should().Be(Today.AddDays(345));
    }

    [Fact]
    public async Task A_farm_whose_latest_test_has_no_date_is_not_listed()
    {
        var w = await World.CreateAsync();
        var farm = w.Farm("Undated");
        w.Test(farm, w.CompanyA, 500, -100);
        w.Test(farm, w.CompanyA, 20, null); // e.g. signed off before dates were recorded
        await w.Db.SaveChangesAsync();

        var page = PageAs(w.Db, "admin-a", Roles.CompanyAdministrator);
        page.Window = 0;
        await page.OnGetAsync();
        page.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Uses_the_current_version_and_ignores_drafts()
    {
        var w = await World.CreateAsync();
        var farm = w.Farm("Amended");
        var v1Client = Guid.NewGuid();
        w.Test(farm, w.CompanyA, 30, 335, clientId: v1Client);
        var v2 = w.Test(farm, w.CompanyA, 5, 335, supersedes: v1Client);
        w.Test(farm, w.CompanyA, 0, 5, complete: false); // an unfinished draft says nothing yet
        await w.Db.SaveChangesAsync();

        var page = PageAs(w.Db, "admin-a", Roles.CompanyAdministrator);
        page.Window = 0;
        await page.OnGetAsync();

        page.Rows.Should().ContainSingle().Which.TestId.Should().Be(v2.Id);
    }

    [Fact]
    public async Task A_company_admin_sees_only_their_company_and_cannot_widen_it()
    {
        var w = await World.CreateAsync();
        w.Test(w.Farm("Ours"), w.CompanyA, 400, -5);
        w.Test(w.Farm("Theirs"), w.CompanyB, 400, -5);
        await w.Db.SaveChangesAsync();

        var page = PageAs(w.Db, "admin-a", Roles.CompanyAdministrator);
        page.CompanyId = w.CompanyB; // a hand-edited query string
        await page.OnGetAsync();

        page.Rows.Select(r => r.FarmName).Should().Equal("Ours");
        page.CompanyId.Should().BeNull();
    }

    [Fact]
    public async Task A_company_admin_without_a_company_sees_nothing()
    {
        var w = await World.CreateAsync();
        w.Test(w.Farm("Someone's"), w.CompanyA, 400, -5);
        await w.Db.SaveChangesAsync();

        var page = PageAs(w.Db, "admin-none", Roles.CompanyAdministrator);
        await page.OnGetAsync();

        page.NoCompany.Should().BeTrue();
        page.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task A_super_admin_sees_every_company_and_can_narrow_to_one()
    {
        var w = await World.CreateAsync();
        w.Test(w.Farm("A farm"), w.CompanyA, 400, -5);
        w.Test(w.Farm("B farm"), w.CompanyB, 400, 3);
        await w.Db.SaveChangesAsync();

        var page = PageAs(w.Db, "super", Roles.SuperAdministrator);
        await page.OnGetAsync();
        page.Rows.Select(r => r.CompanyName).Should().Equal("Company A", "Company B");

        page.CompanyId = w.CompanyB;
        await page.OnGetAsync();
        page.Rows.Select(r => r.FarmName).Should().Equal("B farm");
    }

    // The page runs on SQL Server in production and on the in-memory provider in these tests; this
    // proves the query (NOT EXISTS for "no later test", the superseded-version subquery, the company
    // name lookup) translates to SQL rather than failing at runtime. No connection is opened.
    [Fact]
    public void The_query_translates_to_sql_server()
    {
        using var db = new AutorepDbContext(new DbContextOptionsBuilder<AutorepDbContext>()
            .UseSqlServer("Server=unused;Database=unused;Trusted_Connection=True").Options);

        var sql = UpcomingModel.Query(db, Guid.NewGuid(), Today.AddDays(90)).ToQueryString();

        sql.Should().Contain("NOT EXISTS").And.Contain("[NextTestDate]");
        UpcomingModel.Query(db, null, null).ToQueryString().Should().NotBeNullOrEmpty();
    }

    // ---- Sync --------------------------------------------------------------------------------

    [Fact]
    public async Task Sync_stores_the_next_test_date_and_an_older_device_leaves_it_alone()
    {
        var client = _factory.CreateClientAs(Roles.Tester, "upcoming-sync-tester");
        var clientId = Guid.NewGuid();
        var withDate = new { clientId, farmName = "Sync Due Farm", markedCompleteAt = DateTimeOffset.UtcNow, nextTestDate = "2027-09-15" };
        (await client.PostAsJsonAsync("/api/sync/tests", withDate)).StatusCode.Should().Be(HttpStatusCode.Created);

        // A device that predates the field re-syncs the same test without it.
        var olderShape = new { clientId, farmName = "Sync Due Farm", markedCompleteAt = DateTimeOffset.UtcNow };
        (await client.PostAsJsonAsync("/api/sync/tests", olderShape)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
        (await db.MachineTests.SingleAsync(t => t.ClientId == clientId)).NextTestDate.Should().Be(new DateOnly(2027, 9, 15));
    }
}
