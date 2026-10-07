using System.Net;
using System.Net.Http.Json;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Autorep.Web.Tests;

public class FarmsControllerTests : IClassFixture<AuthedWebAppFactory>
{
    private readonly AuthedWebAppFactory _factory;
    public FarmsControllerTests(AuthedWebAppFactory factory) => _factory = factory;

    private sealed record FarmResponse(
        Guid Id, string Name, string? SupplyNumber, string? Town,
        string? RegionName, string? MilkCompanyName, string? FarmerName);

    [Fact]
    public async Task Get_returns_farm_details_with_region_and_company_names()
    {
        Guid farmId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
            var region = new Region { Name = "Waikato " + Guid.NewGuid(), Island = "North Island" };
            var company = new MilkSupplyCompany { Name = "Fonterra " + Guid.NewGuid() };
            db.Regions.Add(region);
            db.MilkSupplyCompanies.Add(company);
            var farm = new Farm
            {
                Name = "Detail Farm",
                SupplyNumber = "12345",
                Town = "Hamilton",
                RegionId = region.Id,
                MilkSupplyCompanyId = company.Id,
                FarmerName = "Jo Farmer",
            };
            db.Farms.Add(farm);
            // A tester may only fetch a farm they (or their company) have tested.
            db.MachineTests.Add(new MachineTest
            {
                TesterId = "tester-farm-1",
                FarmId = farm.Id,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
            farmId = farm.Id;
        }

        var client = _factory.CreateClientAs(Roles.Tester, "tester-farm-1");
        var dto = await client.GetFromJsonAsync<FarmResponse>($"/api/farms/{farmId}");

        dto.Should().NotBeNull();
        dto!.Name.Should().Be("Detail Farm");
        dto.SupplyNumber.Should().Be("12345");
        dto.Town.Should().Be("Hamilton");
        dto.RegionName.Should().StartWith("Waikato");
        dto.MilkCompanyName.Should().StartWith("Fonterra");
        dto.FarmerName.Should().Be("Jo Farmer");
    }

    [Fact]
    public async Task Get_returns_404_for_an_unknown_farm()
    {
        var client = _factory.CreateClientAs(Roles.Tester, "tester-farm-2");
        var res = await client.GetAsync($"/api/farms/{Guid.NewGuid()}");
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // The shared FarmScope predicate includes farms the company set up but hasn't tested yet,
    // so the wizard can snapshot a freshly added farm before its first test is synced.
    [Fact]
    public async Task Get_returns_a_farm_created_by_the_testers_company_even_before_any_test()
    {
        Guid farmId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
            var company = new TestingCompany { Name = "Created-By Co " + Guid.NewGuid() };
            db.TestingCompanies.Add(company);
            db.Users.Add(new Tester { Id = "tester-createdby-1", UserName = "tester-createdby-1", TestingCompanyId = company.Id });
            var farm = new Farm { Name = "Freshly Added Farm", CreatedByTestingCompanyId = company.Id };
            db.Farms.Add(farm); // deliberately no MachineTest yet
            await db.SaveChangesAsync();
            farmId = farm.Id;
        }

        var client = _factory.CreateClientAs(Roles.Tester, "tester-createdby-1");
        var dto = await client.GetFromJsonAsync<FarmResponse>($"/api/farms/{farmId}");

        dto.Should().NotBeNull();
        dto!.Name.Should().Be("Freshly Added Farm");
    }

    [Fact]
    public async Task Get_returns_404_for_a_farm_the_tester_has_no_relationship_with()
    {
        Guid farmId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
            var farm = new Farm { Name = "Other-Co Farm", FarmerName = "Private Contact" };
            db.Farms.Add(farm);
            // Linked only to a different tester — must not be harvestable by an unrelated tester.
            db.MachineTests.Add(new MachineTest
            {
                TesterId = "some-other-tester",
                FarmId = farm.Id,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
            farmId = farm.Id;
        }

        var client = _factory.CreateClientAs(Roles.Tester, "tester-no-access");
        var res = await client.GetAsync($"/api/farms/{farmId}");
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task List_returns_only_farms_in_the_callers_scope()
    {
        Guid mineId, othersId, inactiveId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();

            var mine = new Farm { Name = "List Mine Farm" };
            var others = new Farm { Name = "List Others Farm" };
            var inactive = new Farm { Name = "List Inactive Farm", IsActive = false };
            db.Farms.AddRange(mine, others, inactive);
            db.MachineTests.AddRange(
                new MachineTest { TesterId = "tester-list-1", FarmId = mine.Id, CreatedAt = DateTimeOffset.UtcNow },
                new MachineTest { TesterId = "tester-list-other", FarmId = others.Id, CreatedAt = DateTimeOffset.UtcNow },
                new MachineTest { TesterId = "tester-list-1", FarmId = inactive.Id, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            (mineId, othersId, inactiveId) = (mine.Id, others.Id, inactive.Id);
        }

        var client = _factory.CreateClientAs(Roles.Tester, "tester-list-1");
        var farms = await client.GetFromJsonAsync<List<FarmResponse>>("/api/farms");

        farms.Should().NotBeNull();
        farms!.Should().Contain(f => f.Id == mineId);
        farms.Should().NotContain(f => f.Id == othersId);   // another tester's farm — not harvestable
        farms.Should().NotContain(f => f.Id == inactiveId); // deactivated farms drop out of the book
    }
    private async Task<Guid> SeedCompanyTesterAsync(string testerId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
        var company = new TestingCompany { Name = "Co " + testerId };
        db.TestingCompanies.Add(company);
        db.Users.Add(new Tester { Id = testerId, UserName = testerId, TestingCompanyId = company.Id });
        await db.SaveChangesAsync();
        return company.Id;
    }

    private async Task<Guid> SeedFarmAsync(string name, Guid? createdByCompany)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
        var farm = new Farm { Name = name, CreatedByTestingCompanyId = createdByCompany };
        db.Farms.Add(farm);
        await db.SaveChangesAsync();
        return farm.Id;
    }

    // The farm book is refreshed on every tester page and is the biggest thing they fetch: an
    // unchanged book must cost a bodyless 304, and any change to it a full answer.
    [Fact]
    public async Task List_answers_304_while_the_book_is_unchanged_and_in_full_once_it_changes()
    {
        var companyId = await SeedCompanyTesterAsync("tester-etag-1");
        await SeedFarmAsync("ETag Farm One", companyId);
        var client = _factory.CreateClientAs(Roles.Tester, "tester-etag-1");

        var first = await client.GetAsync("/api/farms");
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var etag = first.Headers.ETag;
        etag.Should().NotBeNull();

        var again = new HttpRequestMessage(HttpMethod.Get, "/api/farms");
        again.Headers.IfNoneMatch.Add(etag!);
        var unchanged = await client.SendAsync(again);
        unchanged.StatusCode.Should().Be(HttpStatusCode.NotModified);
        (await unchanged.Content.ReadAsByteArrayAsync()).Should().BeEmpty();

        await SeedFarmAsync("ETag Farm Two", companyId);
        var afterChange = new HttpRequestMessage(HttpMethod.Get, "/api/farms");
        afterChange.Headers.IfNoneMatch.Add(etag!);
        var changed = await client.SendAsync(afterChange);
        changed.StatusCode.Should().Be(HttpStatusCode.OK);
        changed.Headers.ETag.Should().NotBe(etag);
        (await changed.Content.ReadFromJsonAsync<List<FarmResponse>>())!
            .Select(f => f.Name).Should().Contain(["ETag Farm One", "ETag Farm Two"]);
    }

    // Farmers' contact details on a shared device: the conditional request is the device's to make
    // from IndexedDB; the browser's own HTTP cache must never keep a copy.
    [Fact]
    public async Task List_is_never_kept_by_the_browser_cache()
    {
        await SeedCompanyTesterAsync("tester-etag-2");
        var client = _factory.CreateClientAs(Roles.Tester, "tester-etag-2");

        var res = await client.GetAsync("/api/farms");

        res.Headers.CacheControl!.NoStore.Should().BeTrue();
        res.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    // The migrated-farm shape: no CreatedBy company, but tested by a colleague. It must be in the
    // book (the scope's test-history leg), or the device could never start a test on it — this
    // was a New-test page check while that page was server-rendered.
    [Fact]
    public async Task List_includes_a_farm_in_scope_only_through_a_colleagues_test_history()
    {
        var companyId = await SeedCompanyTesterAsync("tester-history-1");
        Guid farmId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
            db.Users.Add(new Tester { Id = "tester-history-2", UserName = "tester-history-2", TestingCompanyId = companyId });
            var farm = new Farm { Name = "Legacy History Farm" };
            db.Farms.Add(farm);
            db.MachineTests.Add(new MachineTest { TesterId = "tester-history-2", FarmId = farm.Id });
            await db.SaveChangesAsync();
            farmId = farm.Id;
        }

        var farms = await _factory.CreateClientAs(Roles.Tester, "tester-history-1")
            .GetFromJsonAsync<List<FarmResponse>>("/api/farms");

        farms!.Should().Contain(f => f.Id == farmId);
    }

    private sealed record Option(Guid Id, string Name, string? Island);
    private sealed record Options(List<Option> Regions, List<Option> MilkCompanies);

    [Fact]
    public async Task New_farm_options_offer_only_active_regions_and_milk_companies()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
            db.Regions.AddRange(
                new Region { Name = "Opts Live Region", Island = "South Island" },
                new Region { Name = "Opts Retired Region", Island = "South Island", IsActive = false });
            db.MilkSupplyCompanies.AddRange(
                new MilkSupplyCompany { Name = "Opts Live Co" },
                new MilkSupplyCompany { Name = "Opts Retired Co", IsActive = false });
            await db.SaveChangesAsync();
        }

        var options = await _factory.CreateClientAs(Roles.Tester, "tester-opts")
            .GetFromJsonAsync<Options>("/api/farms/new-farm-options");

        options!.Regions.Should().Contain(r => r.Name == "Opts Live Region" && r.Island == "South Island");
        options.Regions.Should().NotContain(r => r.Name == "Opts Retired Region");
        options.MilkCompanies.Should().Contain(c => c.Name == "Opts Live Co");
        options.MilkCompanies.Should().NotContain(c => c.Name == "Opts Retired Co");
    }

    // The page used to inline the company's whole farm book. Now it's drawn on the device, and the
    // HTML the server sends — the kind of document a device might cache — names no farm at all.
    [Fact]
    public async Task The_New_test_page_carries_no_farm_data()
    {
        var companyId = await SeedCompanyTesterAsync("tester-newpage");
        await SeedFarmAsync("Should Not Appear Farm", companyId);

        var res = await _factory.CreateClientAs(Roles.Tester, "tester-newpage").GetAsync("/App/Tests/New");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await res.Content.ReadAsStringAsync();
        html.Should().Contain("id=\"new-test-root\"");
        html.Should().NotContain("Should Not Appear Farm");
    }
}
