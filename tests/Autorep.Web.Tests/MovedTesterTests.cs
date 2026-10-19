using System.Net;
using System.Net.Http.Json;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Autorep.Web.Tests;

// A tester moves from one Testing Company to another (Josh, 19 Oct: "farms stay behind"). Their tests
// already stay with the company stamped on them (CompanyTestVisibilityTests); the farms they tested
// stay too, because farm scope follows those stamps rather than the tester's current company. Their
// device can still hold the old farms' ids, and testing one again links to it rather than minting a
// duplicate; the new company then gains that farm through the new test.
public class MovedTesterTests : IClassFixture<AuthedWebAppFactory>
{
    private readonly AuthedWebAppFactory _factory;
    public MovedTesterTests(AuthedWebAppFactory factory) => _factory = factory;

    private sealed record FarmResponse(Guid Id, string Name);

    /// <summary>Old company A and new company B, a tester who tested a farm for A and has since
    /// moved to B, and an administrator at each. The farm has no CreatedBy company (the migrated
    /// shape), so A holds it only through the tester's test.</summary>
    private sealed record World(string Tag, Guid CompanyA, Guid CompanyB, string Tester, string AdminA, string AdminB, Guid Farm, string FarmName);

    private async Task<World> MovedTesterAsync()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var world = await WithDbAsync(async db =>
        {
            var a = new TestingCompany { Name = $"Old Co {tag}" };
            var b = new TestingCompany { Name = $"New Co {tag}" };
            db.TestingCompanies.AddRange(a, b);
            var farm = new Farm { Name = $"Left Behind Farm {tag}" };
            db.Farms.Add(farm);
            var w = new World(tag, a.Id, b.Id, $"mover-{tag}", $"admin-a-{tag}", $"admin-b-{tag}", farm.Id, farm.Name);
            db.Users.AddRange(
                new Tester { Id = w.Tester, UserName = w.Tester, DisplayName = $"Mover {tag}", TestingCompanyId = a.Id },
                new Tester { Id = w.AdminA, UserName = w.AdminA, TestingCompanyId = a.Id },
                new Tester { Id = w.AdminB, UserName = w.AdminB, TestingCompanyId = b.Id });
            db.MachineTests.Add(new MachineTest
            {
                TesterId = w.Tester, FarmId = farm.Id, TestingCompanyId = a.Id,
                ClientId = Guid.NewGuid(), MarkedCompleteAt = DateTimeOffset.UtcNow.AddDays(-30),
            });
            await db.SaveChangesAsync();
            return w;
        });

        await WithDbAsync(async db =>
        {
            (await db.Users.SingleAsync(u => u.Id == world.Tester)).TestingCompanyId = world.CompanyB;
            await db.SaveChangesAsync();
            return true;
        });
        return world;
    }

    // ---- Farm scope ---------------------------------------------------------------------------

    [Fact]
    public async Task The_farm_stays_in_the_old_companys_scope_and_not_the_new_ones()
    {
        var w = await MovedTesterAsync();

        var (inA, inB) = await WithDbAsync(async db => (
            await db.Farms.Where(f => f.Id == w.Farm).InCompanyScope(db, w.CompanyA, w.AdminA).AnyAsync(),
            await db.Farms.Where(f => f.Id == w.Farm).InCompanyScope(db, w.CompanyB, w.AdminB).AnyAsync()));

        inA.Should().BeTrue("the test was done for the old company");
        inB.Should().BeFalse("moving the tester doesn't take the farm along");
    }

    [Fact]
    public async Task The_moved_testers_farm_book_holds_the_new_companys_farms_only()
    {
        var w = await MovedTesterAsync();

        var farms = await _factory.CreateClientAs(Roles.Tester, w.Tester).GetFromJsonAsync<List<FarmResponse>>("/api/farms");

        farms!.Should().NotContain(f => f.Id == w.Farm);
    }

    [Fact]
    public async Task Admin_farm_pages_follow_the_tests_company()
    {
        var w = await MovedTesterAsync();

        var oldList = await _factory.CreateClientAs(Roles.CompanyAdministrator, w.AdminA).GetStringAsync("/Admin/Farms");
        var newList = await _factory.CreateClientAs(Roles.CompanyAdministrator, w.AdminB).GetStringAsync("/Admin/Farms");
        oldList.Should().Contain(w.FarmName);
        newList.Should().NotContain(w.FarmName);

        (await _factory.CreateClientAs(Roles.CompanyAdministrator, w.AdminA).GetAsync($"/Admin/Farms/Edit/{w.Farm}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await _factory.CreateClientAs(Roles.CompanyAdministrator, w.AdminB).GetAsync($"/Admin/Farms/Edit/{w.Farm}"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Old_company_keeps_the_testers_test_count_and_dropdown_entry()
    {
        var w = await MovedTesterAsync();

        var farmPage = await _factory.CreateClientAs(Roles.CompanyAdministrator, w.AdminA).GetStringAsync($"/Admin/Farms/Edit/{w.Farm}");
        farmPage.Should().Contain("1 test(s)");

        var testsPage = await _factory.CreateClientAs(Roles.CompanyAdministrator, w.AdminA).GetStringAsync("/Admin/Tests");
        testsPage.Should().Contain($"value=\"{w.Tester}\"", "a tester who has moved on can still be picked to filter their old tests");
        var newTestsPage = await _factory.CreateClientAs(Roles.CompanyAdministrator, w.AdminB).GetStringAsync("/Admin/Tests");
        newTestsPage.Should().Contain($"value=\"{w.Tester}\"", "they work for the new company now");

        var companies = await _factory.CreateClientAs(Roles.SuperAdministrator, "moved-super").GetStringAsync("/Admin/Companies");
        CompanyRowCells(companies, $"Old Co {w.Tag}")[2..4].Should().Equal("1", "1"); // users, tests
        CompanyRowCells(companies, $"New Co {w.Tag}")[2..4].Should().Equal("2", "0");
    }

    // ---- Sync: the device still holds the old farm ids ----------------------------------------

    [Fact]
    public async Task Testing_a_farm_they_tested_before_links_to_it_and_brings_it_into_the_new_company()
    {
        var w = await MovedTesterAsync();
        var clientId = Guid.NewGuid();

        var post = await _factory.CreateClientAs(Roles.Tester, w.Tester).PostAsJsonAsync("/api/sync/tests",
            UploadPayload(clientId, w.FarmName, w.Farm));
        post.StatusCode.Should().Be(HttpStatusCode.Created);

        var (linkedFarm, stamped, farmsNamed) = await WithDbAsync(async db =>
        {
            var test = await db.MachineTests.SingleAsync(t => t.TesterId == w.Tester && t.ClientId == clientId);
            return (test.FarmId, test.TestingCompanyId, await db.Farms.CountAsync(f => f.Name == w.FarmName));
        });
        linkedFarm.Should().Be(w.Farm, "no duplicate farm is minted");
        farmsNamed.Should().Be(1);
        stamped.Should().Be(w.CompanyB, "the new test is work for the new company");

        var inB = await WithDbAsync(db => db.Farms.Where(f => f.Id == w.Farm).InCompanyScope(db, w.CompanyB, w.AdminB).AnyAsync());
        inB.Should().BeTrue("the new company gains the farm through the new test");
    }

    [Fact]
    public async Task An_old_company_farm_they_never_tested_is_still_out_of_reach()
    {
        var w = await MovedTesterAsync();
        var untested = await WithDbAsync(async db =>
        {
            var farm = new Farm { Name = "Old Co's other farm " + Guid.NewGuid(), CreatedByTestingCompanyId = w.CompanyA };
            db.Farms.Add(farm);
            await db.SaveChangesAsync();
            return farm;
        });
        var clientId = Guid.NewGuid();

        (await _factory.CreateClientAs(Roles.Tester, w.Tester).PostAsJsonAsync("/api/sync/tests",
            UploadPayload(clientId, untested.Name, untested.Id))).StatusCode.Should().Be(HttpStatusCode.Created);

        var linked = await WithDbAsync(db => db.MachineTests
            .Where(t => t.TesterId == w.Tester && t.ClientId == clientId).Select(t => t.FarmId).SingleAsync());
        linked.Should().NotBe(untested.Id, "a push can't attach a test to another company's farm");
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static object UploadPayload(Guid clientId, string farmName, Guid farmId) => new
    {
        clientId,
        farmName,
        farmId,
        notes = (string?)null,
        markedCompleteAt = (DateTimeOffset?)null,
        createdAt = DateTimeOffset.UtcNow,
        config = (object?)null,
    };

    /// <summary>The Companies list row's cells after the name (tester count, test count, …).</summary>
    private static List<string> CompanyRowCells(string html, string companyName)
    {
        var row = System.Text.RegularExpressions.Regex.Match(html,
            $"<tr>(?:(?!</tr>).)*{System.Text.RegularExpressions.Regex.Escape(companyName)}(?:(?!</tr>).)*</tr>",
            System.Text.RegularExpressions.RegexOptions.Singleline).Value;
        row.Should().NotBeEmpty($"the list has a row for {companyName}");
        return System.Text.RegularExpressions.Regex.Matches(row, "<td[^>]*>\\s*(.*?)\\s*</td>",
                System.Text.RegularExpressions.RegexOptions.Singleline)
            .Select(m => System.Text.RegularExpressions.Regex.Replace(m.Groups[1].Value, "<[^>]+>", "").Trim())
            .ToList();
    }

    private async Task<T> WithDbAsync<T>(Func<AutorepDbContext, Task<T>> f)
    {
        using var scope = _factory.Services.CreateScope();
        return await f(scope.ServiceProvider.GetRequiredService<AutorepDbContext>());
    }
}
