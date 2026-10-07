using System.Net;
using System.Text.RegularExpressions;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Autorep.Web.Tests.TestPayloads;

namespace Autorep.Web.Tests;

// O2: /Admin/Tests filters on the server over the paginated query — tester, company, farm or tester
// search, tested-date range (New Zealand days), status, has conflicts, show deleted — one row per test
// (its current version), with each active filter as a chip that removes just that one.
public partial class AdminTestListFilterTests : IClassFixture<AuthedWebAppFactory>
{
    private readonly AuthedWebAppFactory _factory;
    public AdminTestListFilterTests(AuthedWebAppFactory factory) => _factory = factory;

    private IServiceProvider Services => _factory.Services;

    private async Task<T> WithDbAsync<T>(Func<AutorepDbContext, Task<T>> f)
    {
        using var scope = Services.CreateScope();
        return await f(scope.ServiceProvider.GetRequiredService<AutorepDbContext>());
    }

    private async Task<Farm> FarmAsync(Guid companyId, string name, string? supplyNumber = null) =>
        await WithDbAsync(async db =>
        {
            var farm = new Farm { Name = $"{name} {Guid.NewGuid():N}", SupplyNumber = supplyNumber, CreatedByTestingCompanyId = companyId };
            db.Farms.Add(farm);
            await db.SaveChangesAsync();
            return farm;
        });

    private Task<MachineTest> TestAsync(string testerId, Farm farm, Guid companyId, bool complete = true,
        DateTimeOffset? completedAt = null, int version = 1, Guid? supersedes = null, Guid? root = null) =>
        SeedAndStampAsync(testerId, farm, companyId, complete, completedAt, version, supersedes, root);

    private async Task<MachineTest> SeedAndStampAsync(string testerId, Farm farm, Guid companyId, bool complete,
        DateTimeOffset? completedAt, int version, Guid? supersedes, Guid? root)
    {
        var test = await SeedTestAsync(Services, testerId, farm.Id, companyId, Original(Guid.NewGuid(), farm.Name), complete,
            version, supersedes: supersedes, root: root);
        if (completedAt is { } at)
            await WithDbAsync(async db =>
            {
                (await db.MachineTests.SingleAsync(t => t.Id == test.Id)).MarkedCompleteAt = at;
                return await db.SaveChangesAsync();
            });
        return test;
    }

    private HttpClient SuperAdmin() => _factory.CreateClientAs(Roles.SuperAdministrator, "filters-superadmin");

    private async Task<string> ListAsync(HttpClient client, string query)
    {
        var res = await client.GetAsync($"/Admin/Tests?{query}");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        return WebUtility.HtmlDecode(await res.Content.ReadAsStringAsync());
    }

    /// <summary>The page's chips: what each says, and where its × goes.</summary>
    private static List<(string Label, string Href)> Chips(string html) =>
        ChipPattern().Matches(html)
            .Select(m => (Regex.Replace(m.Groups["label"].Value, @"\s+", " ").Trim(), m.Groups["href"].Value))
            .ToList();

    [GeneratedRegex("""<a class="filter-chip"[^>]*?\shref="(?<href>[^"]*)"[^>]*>\s*(?<label>[^<]*?)\s*<span class="filter-chip__x""")]
    private static partial Regex ChipPattern();

    [Fact]
    public async Task Status_filter_shows_only_complete_or_in_progress_tests()
    {
        var (company, _) = await SeedCompanyAsync(Services, "flt-status");
        await SeedUserAsync(Services, "flt-status-tester", company);
        var done = await FarmAsync(company, "Done Farm");
        var going = await FarmAsync(company, "Going Farm");
        await TestAsync("flt-status-tester", done, company);
        await TestAsync("flt-status-tester", going, company, complete: false);

        var complete = await ListAsync(SuperAdmin(), $"companyId={company}&status=complete");
        complete.Should().Contain(done.Name).And.NotContain(going.Name);

        var inProgress = await ListAsync(SuperAdmin(), $"companyId={company}&status=in-progress");
        inProgress.Should().Contain(going.Name).And.NotContain(done.Name);

        var any = await ListAsync(SuperAdmin(), $"companyId={company}&status=bogus");
        any.Should().Contain(done.Name).And.Contain(going.Name);
    }

    [Fact]
    public async Task Has_conflicts_shows_only_tests_whose_history_holds_a_collision()
    {
        var (company, _) = await SeedCompanyAsync(Services, "flt-conflict");
        await SeedUserAsync(Services, "flt-conflict-tester", company);
        var clashed = await FarmAsync(company, "Clashed Farm");
        var calm = await FarmAsync(company, "Calm Farm");
        var v1 = await TestAsync("flt-conflict-tester", clashed, company);
        // The current version of the clashed test is version 2; the conflict is keyed by the lineage.
        await TestAsync("flt-conflict-tester", clashed, company, version: 2, supersedes: v1.ClientId, root: v1.ClientId);
        await TestAsync("flt-conflict-tester", calm, company);
        await WithDbAsync(async db =>
        {
            db.SyncConflicts.Add(new SyncConflict
            {
                TesterId = "flt-conflict-tester", RootClientId = v1.ClientId,
                BaseClientId = v1.ClientId!.Value, HeadClientId = Guid.NewGuid(), IncomingClientId = Guid.NewGuid(),
            });
            return await db.SaveChangesAsync();
        });

        var all = await ListAsync(SuperAdmin(), $"companyId={company}");
        all.Should().Contain(clashed.Name).And.Contain(calm.Name);
        Regex.Matches(all, ">Conflict<").Should().ContainSingle("the clashed test is flagged in the full list too");

        var conflicted = await ListAsync(SuperAdmin(), $"companyId={company}&hasConflicts=true");
        conflicted.Should().Contain(clashed.Name).And.NotContain(calm.Name);
        Regex.Matches(conflicted, "<tr>").Count.Should().Be(2, "one header row and one test — not one per version");
    }

    [Fact]
    public async Task The_date_range_is_in_new_zealand_days_and_uses_the_completion_date()
    {
        var (company, _) = await SeedCompanyAsync(Services, "flt-dates");
        await SeedUserAsync(Services, "flt-dates-tester", company);
        var lateOn10th = await FarmAsync(company, "Late Tenth Farm");
        var earlyOn11th = await FarmAsync(company, "Early Eleventh Farm");
        var on12th = await FarmAsync(company, "Twelfth Farm");
        // NZDT is UTC+13 in March: 10:30Z is 11:30 pm on the 10th; 11:30Z is 12:30 am on the 11th.
        await TestAsync("flt-dates-tester", lateOn10th, company, completedAt: new DateTimeOffset(2026, 3, 10, 10, 30, 0, TimeSpan.Zero));
        await TestAsync("flt-dates-tester", earlyOn11th, company, completedAt: new DateTimeOffset(2026, 3, 10, 11, 30, 0, TimeSpan.Zero));
        await TestAsync("flt-dates-tester", on12th, company, completedAt: new DateTimeOffset(2026, 3, 11, 23, 0, 0, TimeSpan.Zero));

        var the11th = await ListAsync(SuperAdmin(), $"companyId={company}&from=2026-03-11&to=2026-03-11");
        the11th.Should().Contain(earlyOn11th.Name).And.NotContain(lateOn10th.Name).And.NotContain(on12th.Name);

        var from11th = await ListAsync(SuperAdmin(), $"companyId={company}&from=2026-03-11");
        from11th.Should().Contain(earlyOn11th.Name).And.Contain(on12th.Name).And.NotContain(lateOn10th.Name);

        var upTo10th = await ListAsync(SuperAdmin(), $"companyId={company}&to=2026-03-10");
        upTo10th.Should().Contain(lateOn10th.Name).And.NotContain(earlyOn11th.Name);

        // The calendar's own ends are valid dates that exclude nothing — not a 500.
        var everything = await ListAsync(SuperAdmin(), $"companyId={company}&from=0001-01-01&to=9999-12-31");
        everything.Should().Contain(lateOn10th.Name).And.Contain(earlyOn11th.Name).And.Contain(on12th.Name);
    }

    [Fact]
    public async Task Tester_filter_and_search_by_supply_number_or_tester_email()
    {
        var (company, _) = await SeedCompanyAsync(Services, "flt-who");
        await SeedUserAsync(Services, "flt-who-ana", company, "Ana Aroha");
        await SeedUserAsync(Services, "flt-who-ben", company, "Ben Bryant");
        var anas = await FarmAsync(company, "Ana Farm", supplyNumber: "77001");
        var bens = await FarmAsync(company, "Ben Farm", supplyNumber: "88002");
        await TestAsync("flt-who-ana", anas, company);
        await TestAsync("flt-who-ben", bens, company);

        var byTester = await ListAsync(SuperAdmin(), $"companyId={company}&testerId=flt-who-ben");
        byTester.Should().Contain(bens.Name).And.NotContain(anas.Name);

        var bySupply = await ListAsync(SuperAdmin(), $"companyId={company}&q=77001");
        bySupply.Should().Contain(anas.Name).And.NotContain(bens.Name);

        var byEmail = await ListAsync(SuperAdmin(), $"companyId={company}&q=flt-who-ben@local");
        byEmail.Should().Contain(bens.Name).And.NotContain(anas.Name);
    }

    [Fact]
    public async Task Each_chip_removes_only_its_own_filter()
    {
        var (company, _) = await SeedCompanyAsync(Services, "flt-chips");
        await SeedUserAsync(Services, "flt-chips-tester", company, "Chip Tester");

        var html = await ListAsync(SuperAdmin(),
            $"companyId={company}&testerId=flt-chips-tester&status=complete&hasConflicts=true&from=2026-01-01&showDeleted=true&q=kowhai");

        var chips = Chips(html);
        chips.Select(c => c.Label).Should().BeEquivalentTo(
            "Search: kowhai", $"Company: {await CompanyNameAsync(company)}", "Tester: Chip Tester",
            "Tested from 1 Jan 2026", "Complete", "Has conflicts", "Including deleted");
        var status = chips.Single(c => c.Label == "Complete").Href;
        status.Should().NotContain("status=").And.Contain("hasConflicts=true").And.Contain("testerId=flt-chips-tester")
            .And.Contain($"companyId={company}").And.Contain("from=2026-01-01").And.Contain("showDeleted=true").And.Contain("q=kowhai");
        chips.Single(c => c.Label == "Has conflicts").Href.Should().NotContain("hasConflicts").And.Contain("status=complete");
        html.Should().Contain("Clear all");
        html.Should().Contain("No matching tests");
    }

    private Task<string> CompanyNameAsync(Guid id) =>
        WithDbAsync(db => db.TestingCompanies.Where(c => c.Id == id).Select(c => c.Name).SingleAsync());

    [Fact]
    public async Task Paging_keeps_the_filters()
    {
        var (company, _) = await SeedCompanyAsync(Services, "flt-pages");
        await SeedUserAsync(Services, "flt-pages-tester", company);
        var farm = await FarmAsync(company, "Paged Farm");
        for (var i = 0; i < 51; i++) await TestAsync("flt-pages-tester", farm, company);

        var first = await ListAsync(SuperAdmin(), $"companyId={company}&status=complete");
        first.Should().Contain("Page 1 of 2 · 51 total");
        var next = Regex.Match(first, """href="(?<href>[^"]*)">Next ›""").Groups["href"].Value;
        next.Should().Contain("pageNumber=2").And.Contain("status=complete").And.Contain($"companyId={company}");

        var second = await ListAsync(SuperAdmin(), next[(next.IndexOf('?') + 1)..]);
        second.Should().Contain("Page 2 of 2");
        Regex.Matches(second, "<tr>").Count.Should().Be(2, "a header row and the 51st test");
    }

    [Fact]
    public async Task A_company_administrator_cannot_widen_the_list_to_another_company()
    {
        var (mine, _) = await SeedCompanyAsync(Services, "flt-scope-mine");
        var (theirs, _) = await SeedCompanyAsync(Services, "flt-scope-theirs");
        await SeedUserAsync(Services, "flt-scope-admin", mine);
        await SeedUserAsync(Services, "flt-scope-mytester", mine);
        await SeedUserAsync(Services, "flt-scope-theirtester", theirs);
        var myFarm = await FarmAsync(mine, "My Farm");
        var theirFarm = await FarmAsync(theirs, "Their Farm");
        await TestAsync("flt-scope-mytester", myFarm, mine);
        await TestAsync("flt-scope-theirtester", theirFarm, theirs);
        var admin = _factory.CreateClientAs(Roles.CompanyAdministrator, "flt-scope-admin");

        var html = await ListAsync(admin, $"companyId={theirs}&testerId=flt-scope-theirtester");

        html.Should().NotContain(theirFarm.Name);
        Chips(html).Select(c => c.Label).Should().NotContain(l => l.StartsWith("Company:"));
        var mineOnly = await ListAsync(admin, $"companyId={theirs}");
        mineOnly.Should().Contain(myFarm.Name).And.NotContain(theirFarm.Name);
        mineOnly.Should().NotContain("flt-scope-theirtester", "another company's testers aren't offered as a filter");
    }
}
