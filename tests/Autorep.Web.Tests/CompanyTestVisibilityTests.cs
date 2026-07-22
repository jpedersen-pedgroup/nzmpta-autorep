using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Autorep.Web.Tests;

// The read boundary for /api/tests: a Tester sees their own tests in any state plus COMPLETED
// tests done for their Testing Company; everything else is NotFound (never Forbid, so neither a
// test's existence nor its completion state leaks through a status code).
public class CompanyTestVisibilityTests : IClassFixture<AuthedWebAppFactory>
{
    private readonly AuthedWebAppFactory _factory;
    public CompanyTestVisibilityTests(AuthedWebAppFactory factory) => _factory = factory;

    private sealed record TestView(
        Guid Id, string FarmName, DateTimeOffset CreatedAt, DateTimeOffset? MarkedCompleteAt,
        object? Config, string? PayloadJson, string? TesterName);

    private sealed record CompanyTestRow(
        Guid Id, string FarmName, string? TesterName, DateTimeOffset CompletedAt,
        int Version, bool IsMine);

    private sealed record CompanyTests(string? CompanyName, int Total, List<CompanyTestRow> Items);

    private async Task<T> WithDbAsync<T>(Func<AutorepDbContext, Task<T>> f)
    {
        using var scope = _factory.Services.CreateScope();
        return await f(scope.ServiceProvider.GetRequiredService<AutorepDbContext>());
    }

    /// <summary>A Testing Company with a farm; returns both ids.</summary>
    private Task<(Guid CompanyId, Guid FarmId)> SeedCompanyAsync(string name) =>
        WithDbAsync(async db =>
        {
            var company = new TestingCompany { Name = $"{name} {Guid.NewGuid()}" };
            db.TestingCompanies.Add(company);
            var farm = new Farm { Name = $"{name} Farm {Guid.NewGuid()}", CreatedByTestingCompanyId = company.Id };
            db.Farms.Add(farm);
            await db.SaveChangesAsync();
            return (company.Id, farm.Id);
        });

    private Task SeedTesterAsync(string testerId, Guid? companyId, string? displayName = null) =>
        WithDbAsync(async db =>
        {
            db.Users.Add(new Tester
            {
                Id = testerId,
                UserName = testerId,
                DisplayName = displayName ?? testerId,
                TestingCompanyId = companyId,
            });
            await db.SaveChangesAsync();
            return true;
        });

    private Task<Guid> SeedTestAsync(
        string testerId, Guid farmId, Guid? companyId, bool complete,
        int version = 1, Guid? clientId = null, Guid? supersedes = null, string? payload = null) =>
        WithDbAsync(async db =>
        {
            var test = new MachineTest
            {
                TesterId = testerId,
                FarmId = farmId,
                TestingCompanyId = companyId,
                ClientId = clientId ?? Guid.NewGuid(),
                MarkedCompleteAt = complete ? DateTimeOffset.UtcNow : null,
                Version = version,
                SupersedesClientId = supersedes,
                PayloadJson = payload,
            };
            db.MachineTests.Add(test);
            await db.SaveChangesAsync();
            return test.Id;
        });

    // ---- Single-test fetch -------------------------------------------------

    [Fact]
    public async Task Tester_can_view_a_colleagues_completed_test_in_the_same_company()
    {
        var (companyId, farmId) = await SeedCompanyAsync("Visible Co");
        await SeedTesterAsync("cv-owner", companyId, "Dave Owner");
        await SeedTesterAsync("cv-viewer", companyId);
        var testId = await SeedTestAsync("cv-owner", farmId, companyId, complete: true, payload: "{\"readings\":{}}");

        var client = _factory.CreateClientAs(Roles.Tester, "cv-viewer");
        var res = await client.GetAsync($"/api/tests/{testId}");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var view = await res.Content.ReadFromJsonAsync<TestView>();
        view!.PayloadJson.Should().Be("{\"readings\":{}}");
        view.TesterName.Should().Be("Dave Owner", "the reader must be able to see whose test it is");
    }

    [Fact]
    public async Task Tester_cannot_view_a_colleagues_in_progress_test()
    {
        var (companyId, farmId) = await SeedCompanyAsync("InProgress Co");
        await SeedTesterAsync("ip-owner", companyId);
        await SeedTesterAsync("ip-viewer", companyId);
        var testId = await SeedTestAsync("ip-owner", farmId, companyId, complete: false);

        var client = _factory.CreateClientAs(Roles.Tester, "ip-viewer");
        var res = await client.GetAsync($"/api/tests/{testId}");

        res.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "unfinished work stays private, and 404 (not 403) keeps completion state from leaking");
    }

    [Fact]
    public async Task Tester_can_still_view_their_own_in_progress_test()
    {
        var (companyId, farmId) = await SeedCompanyAsync("Own Co");
        await SeedTesterAsync("own-1", companyId);
        var testId = await SeedTestAsync("own-1", farmId, companyId, complete: false);

        var client = _factory.CreateClientAs(Roles.Tester, "own-1");
        (await client.GetAsync($"/api/tests/{testId}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Tester_cannot_view_a_completed_test_from_another_company()
    {
        var (companyA, farmA) = await SeedCompanyAsync("Other Co A");
        var (companyB, _) = await SeedCompanyAsync("Other Co B");
        await SeedTesterAsync("oc-owner", companyA);
        await SeedTesterAsync("oc-viewer", companyB);
        var testId = await SeedTestAsync("oc-owner", farmA, companyA, complete: true);

        var client = _factory.CreateClientAs(Roles.Tester, "oc-viewer");
        (await client.GetAsync($"/api/tests/{testId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Two testers with no company must not see each other. Comparing nullable company ids directly
    // would make this pass under SQL Server (null = null is unknown) but leak under the InMemory
    // provider used here (null == null is true) — hence the non-nullable unwrap in TestScope.
    [Fact]
    public async Task Companyless_testers_cannot_see_each_others_tests()
    {
        var (_, farmId) = await SeedCompanyAsync("Unattached Co");
        await SeedTesterAsync("nc-owner", null);
        await SeedTesterAsync("nc-viewer", null);
        var testId = await SeedTestAsync("nc-owner", farmId, null, complete: true);

        var client = _factory.CreateClientAs(Roles.Tester, "nc-viewer");
        (await client.GetAsync($"/api/tests/{testId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Company_admin_can_still_view_an_in_progress_test_in_their_company()
    {
        var (companyId, farmId) = await SeedCompanyAsync("Admin Co");
        await SeedTesterAsync("ac-owner", companyId);
        await SeedTesterAsync("ac-admin", companyId);
        var testId = await SeedTestAsync("ac-owner", farmId, companyId, complete: false);

        var client = _factory.CreateClientAs(Roles.CompanyAdministrator, "ac-admin");
        (await client.GetAsync($"/api/tests/{testId}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Super_admin_can_view_any_test()
    {
        var (companyId, farmId) = await SeedCompanyAsync("Super Co");
        await SeedTesterAsync("sa-owner", companyId);
        var testId = await SeedTestAsync("sa-owner", farmId, companyId, complete: false);

        var client = _factory.CreateClientAs(Roles.SuperAdministrator, "sa-admin");
        (await client.GetAsync($"/api/tests/{testId}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // /api/tests is not role-gated at the class level, so a role that is none of the three must
    // fall through to "nothing" rather than inherit company-wide read.
    [Fact]
    public async Task An_unrecognised_role_sees_nothing()
    {
        var (companyId, farmId) = await SeedCompanyAsync("Unknown Role Co");
        await SeedTesterAsync("ur-owner", companyId);
        await SeedTesterAsync("ur-other", companyId);
        var testId = await SeedTestAsync("ur-owner", farmId, companyId, complete: true);

        var client = _factory.CreateClientAs("SomeFutureRole", "ur-other");
        (await client.GetAsync($"/api/tests/{testId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // The reason TestingCompanyId is stamped at upload instead of joined through the owner: moving
    // a tester between companies must not retro-share their previous employer's work.
    [Fact]
    public async Task Moving_a_tester_to_another_company_does_not_share_their_old_tests()
    {
        var (companyA, farmA) = await SeedCompanyAsync("Move From Co");
        var (companyB, _) = await SeedCompanyAsync("Move To Co");
        await SeedTesterAsync("mv-owner", companyA);
        await SeedTesterAsync("mv-colleague", companyB);
        var testId = await SeedTestAsync("mv-owner", farmA, companyA, complete: true);

        await WithDbAsync(async db =>
        {
            var owner = await db.Users.SingleAsync(u => u.Id == "mv-owner");
            owner.TestingCompanyId = companyB;
            await db.SaveChangesAsync();
            return true;
        });

        var client = _factory.CreateClientAs(Roles.Tester, "mv-colleague");
        (await client.GetAsync($"/api/tests/{testId}")).StatusCode.Should().Be(HttpStatusCode.NotFound,
            "the test stays with the company it was performed for");
    }

    // ---- Company list ------------------------------------------------------

    [Fact]
    public async Task List_returns_completed_company_tests_only_and_marks_own_rows()
    {
        var (companyId, farmId) = await SeedCompanyAsync("List Co");
        var (otherCompany, otherFarm) = await SeedCompanyAsync("List Other Co");
        await SeedTesterAsync("ls-owner", companyId, "Colleague One");
        await SeedTesterAsync("ls-me", companyId, "Me Myself");
        await SeedTesterAsync("ls-outsider", otherCompany);

        var colleagueDone = await SeedTestAsync("ls-owner", farmId, companyId, complete: true);
        var mineDone = await SeedTestAsync("ls-me", farmId, companyId, complete: true);
        await SeedTestAsync("ls-owner", farmId, companyId, complete: false);
        await SeedTestAsync("ls-outsider", otherFarm, otherCompany, complete: true);

        var client = _factory.CreateClientAs(Roles.Tester, "ls-me");
        var body = await client.GetFromJsonAsync<CompanyTests>("/api/tests");

        body!.Items.Select(i => i.Id).Should().BeEquivalentTo(new[] { colleagueDone, mineDone });
        body.Total.Should().Be(2);
        body.Items.Single(i => i.Id == mineDone).IsMine.Should().BeTrue();
        body.Items.Single(i => i.Id == colleagueDone).IsMine.Should().BeFalse();
        body.Items.Single(i => i.Id == colleagueDone).TesterName.Should().Be("Colleague One");
    }

    [Fact]
    public async Task List_hides_superseded_versions()
    {
        var (companyId, farmId) = await SeedCompanyAsync("Version Co");
        await SeedTesterAsync("vs-owner", companyId);
        await SeedTesterAsync("vs-viewer", companyId);

        var v1ClientId = Guid.NewGuid();
        var v1 = await SeedTestAsync("vs-owner", farmId, companyId, complete: true, clientId: v1ClientId);
        var v2 = await SeedTestAsync("vs-owner", farmId, companyId, complete: true,
            version: 2, supersedes: v1ClientId);

        var client = _factory.CreateClientAs(Roles.Tester, "vs-viewer");
        var body = await client.GetFromJsonAsync<CompanyTests>("/api/tests");

        body!.Items.Select(i => i.Id).Should().BeEquivalentTo(new[] { v2 },
            "a withdrawn version must not be offered for reprint alongside the current one");
        body.Items.Single().Version.Should().Be(2);

        // Still reachable by id — the amendment history references it.
        (await client.GetAsync($"/api/tests/{v1}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // A push can carry any SupersedesClientId; the chain is only ever resolved within one tester's
    // rows, so it can't be used to withdraw a colleague's test from the company list.
    [Fact]
    public async Task A_cross_owner_supersedes_claim_does_not_withdraw_the_other_testers_test()
    {
        var (companyId, farmId) = await SeedCompanyAsync("Claim Co");
        await SeedTesterAsync("cl-victim", companyId);
        await SeedTesterAsync("cl-attacker", companyId);

        var victimClientId = Guid.NewGuid();
        var victimTest = await SeedTestAsync("cl-victim", farmId, companyId, complete: true, clientId: victimClientId);
        await SeedTestAsync("cl-attacker", farmId, companyId, complete: true,
            version: 2, supersedes: victimClientId);

        var client = _factory.CreateClientAs(Roles.Tester, "cl-victim");
        var body = await client.GetFromJsonAsync<CompanyTests>("/api/tests");

        body!.Items.Select(i => i.Id).Should().Contain(victimTest);
    }

    [Fact]
    public async Task List_search_matches_farm_and_tester_name()
    {
        var (companyId, _) = await SeedCompanyAsync("Search Co");
        await SeedTesterAsync("sr-owner", companyId, "Wiremu Tester");
        await SeedTesterAsync("sr-viewer", companyId, "Someone Else");
        var (farmA, farmB) = await WithDbAsync(async db =>
        {
            var a = new Farm { Name = "Kereru Downs", CreatedByTestingCompanyId = companyId };
            var b = new Farm { Name = "Totara Flats", CreatedByTestingCompanyId = companyId };
            db.Farms.AddRange(a, b);
            await db.SaveChangesAsync();
            return (a.Id, b.Id);
        });
        var kereru = await SeedTestAsync("sr-viewer", farmA, companyId, complete: true);
        var totara = await SeedTestAsync("sr-owner", farmB, companyId, complete: true);

        var client = _factory.CreateClientAs(Roles.Tester, "sr-viewer");

        var byFarm = await client.GetFromJsonAsync<CompanyTests>("/api/tests?q=Kereru");
        byFarm!.Items.Select(i => i.Id).Should().BeEquivalentTo(new[] { kereru });

        var byTester = await client.GetFromJsonAsync<CompanyTests>("/api/tests?q=Wiremu");
        byTester!.Items.Select(i => i.Id).Should().BeEquivalentTo(new[] { totara });
    }

    [Fact]
    public async Task List_pages_and_caps_the_requested_size()
    {
        var (companyId, farmId) = await SeedCompanyAsync("Paging Co");
        await SeedTesterAsync("pg-me", companyId);
        for (var i = 0; i < 5; i++)
            await SeedTestAsync("pg-me", farmId, companyId, complete: true);

        var client = _factory.CreateClientAs(Roles.Tester, "pg-me");

        var firstPage = await client.GetFromJsonAsync<CompanyTests>("/api/tests?take=2");
        firstPage!.Items.Should().HaveCount(2);
        firstPage.Total.Should().Be(5, "total counts the whole result set, not the page");

        var secondPage = await client.GetFromJsonAsync<CompanyTests>("/api/tests?skip=2&take=2");
        secondPage!.Items.Select(i => i.Id).Should().NotIntersectWith(firstPage.Items.Select(i => i.Id));

        var greedy = await client.GetFromJsonAsync<CompanyTests>("/api/tests?take=100000");
        greedy!.Items.Should().HaveCount(5, "a huge take must clamp rather than bulk-export");
    }

    // Guards against a future Include() dragging the (up to 15 MB) capture payload into the list.
    [Fact]
    public async Task List_never_exposes_the_payload_or_client_id()
    {
        var (companyId, farmId) = await SeedCompanyAsync("Payload Co");
        await SeedTesterAsync("pl-owner", companyId);
        await SeedTesterAsync("pl-viewer", companyId);
        await SeedTestAsync("pl-owner", farmId, companyId, complete: true,
            payload: "{\"farmer\":\"Jane Doe, 027-555-0100\"}");

        var client = _factory.CreateClientAs(Roles.Tester, "pl-viewer");
        var json = await client.GetStringAsync("/api/tests");

        json.Should().NotContain("Jane Doe");
        using var doc = JsonDocument.Parse(json);
        var row = doc.RootElement.GetProperty("items")[0];
        row.TryGetProperty("payloadJson", out _).Should().BeFalse();
        row.TryGetProperty("clientId", out _).Should().BeFalse();
    }

    [Fact]
    public async Task List_ignores_a_company_id_query_parameter()
    {
        var (companyA, farmA) = await SeedCompanyAsync("Tamper A Co");
        var (companyB, farmB) = await SeedCompanyAsync("Tamper B Co");
        await SeedTesterAsync("tp-a", companyA);
        await SeedTesterAsync("tp-b", companyB);
        var aTest = await SeedTestAsync("tp-a", farmA, companyA, complete: true);
        var bTest = await SeedTestAsync("tp-b", farmB, companyB, complete: true);

        var client = _factory.CreateClientAs(Roles.Tester, "tp-a");
        var body = await client.GetFromJsonAsync<CompanyTests>($"/api/tests?companyId={companyB}");

        body!.Items.Select(i => i.Id).Should().BeEquivalentTo(new[] { aTest });
        body.Items.Select(i => i.Id).Should().NotContain(bTest);
    }

    [Fact]
    public async Task A_tester_with_no_company_gets_an_empty_list_not_a_failure()
    {
        await SeedTesterAsync("nl-me", null);

        var client = _factory.CreateClientAs(Roles.Tester, "nl-me");
        var res = await client.GetAsync("/api/tests");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadFromJsonAsync<CompanyTests>();
        body!.CompanyName.Should().BeNull();
        body.Items.Should().BeEmpty();
    }

    // ---- Admin list uses the same company definition ----------------------

    // The Admin all-tests page scopes on the same stamp as the tester surfaces, so "our company's
    // tests" means one thing across the app.
    [Fact]
    public async Task Admin_tests_page_lists_its_own_companys_tests_and_not_another_companys()
    {
        var (companyA, _) = await SeedCompanyAsync("Admin Scope A");
        var (companyB, _) = await SeedCompanyAsync("Admin Scope B");
        var (mineFarm, theirsFarm) = await WithDbAsync(async db =>
        {
            var a = new Farm { Name = "Admin Mine " + Guid.NewGuid(), CreatedByTestingCompanyId = companyA };
            var b = new Farm { Name = "Admin Theirs " + Guid.NewGuid(), CreatedByTestingCompanyId = companyB };
            db.Farms.AddRange(a, b);
            await db.SaveChangesAsync();
            return (a, b);
        });
        await SeedTesterAsync("as-owner", companyA);
        await SeedTesterAsync("as-other", companyB);
        await SeedTesterAsync("as-admin", companyA);
        await SeedTestAsync("as-owner", mineFarm.Id, companyA, complete: true);
        await SeedTestAsync("as-other", theirsFarm.Id, companyB, complete: true);

        var client = _factory.CreateClientAs(Roles.CompanyAdministrator, "as-admin");
        var html = await client.GetStringAsync("/Admin/Tests");

        html.Should().Contain(mineFarm.Name);
        html.Should().NotContain(theirsFarm.Name);
    }

    // ---- Read audit --------------------------------------------------------

    [Fact]
    public async Task Viewing_a_colleagues_test_is_audited_without_the_payload()
    {
        var (companyId, farmId) = await SeedCompanyAsync("Audit Co");
        await SeedTesterAsync("au-owner", companyId);
        await SeedTesterAsync("au-viewer", companyId);
        const string pii = "Jane Farmer, 027-555-0199";
        var testId = await SeedTestAsync("au-owner", farmId, companyId, complete: true,
            payload: $"{{\"farmer\":\"{pii}\"}}");

        var client = _factory.CreateClientAs(Roles.Tester, "au-viewer");
        (await client.GetAsync($"/api/tests/{testId}")).StatusCode.Should().Be(HttpStatusCode.OK);

        var entries = await WithDbAsync(db => db.AuditEntries
            .Where(e => e.Operation == "ViewedByColleague" && e.EntityKey == testId.ToString())
            .ToListAsync());

        entries.Should().ContainSingle();
        entries[0].Actor.Should().Be("au-viewer");
        entries[0].EntityType.Should().Be(nameof(MachineTest));
        entries[0].AfterJson.Should().Contain("au-owner").And.NotContain(pii);
    }

    [Fact]
    public async Task Viewing_your_own_test_is_not_audited()
    {
        var (companyId, farmId) = await SeedCompanyAsync("Self Audit Co");
        await SeedTesterAsync("sf-me", companyId);
        var testId = await SeedTestAsync("sf-me", farmId, companyId, complete: true);

        var client = _factory.CreateClientAs(Roles.Tester, "sf-me");
        (await client.GetAsync($"/api/tests/{testId}")).StatusCode.Should().Be(HttpStatusCode.OK);

        var count = await WithDbAsync(db => db.AuditEntries
            .CountAsync(e => e.Operation == "ViewedByColleague" && e.EntityKey == testId.ToString()));
        count.Should().Be(0);
    }

    // ---- The online-only boundary -----------------------------------------

    // Company-wide READ must never widen the sync pull: colleagues' tests are read online and never
    // land in a device's IndexedDB.
    [Fact]
    public async Task The_sync_pull_still_returns_only_the_callers_own_tests()
    {
        var (companyId, farmId) = await SeedCompanyAsync("Pull Co");
        await SeedTesterAsync("pu-owner", companyId);
        await SeedTesterAsync("pu-viewer", companyId);
        await SeedTestAsync("pu-owner", farmId, companyId, complete: true);
        var mine = await SeedTestAsync("pu-viewer", farmId, companyId, complete: true);

        var client = _factory.CreateClientAs(Roles.Tester, "pu-viewer");
        var json = await client.GetStringAsync("/api/sync/tests");

        using var doc = JsonDocument.Parse(json);
        var pulled = doc.RootElement.GetProperty("tests").EnumerateArray().ToList();
        pulled.Should().HaveCount(1);

        var mineClientId = await WithDbAsync(db => db.MachineTests
            .Where(t => t.Id == mine).Select(t => t.ClientId).SingleAsync());
        pulled[0].GetProperty("clientId").GetGuid().Should().Be(mineClientId!.Value);
    }
}
