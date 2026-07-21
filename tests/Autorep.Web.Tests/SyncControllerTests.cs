using System.Net;
using System.Net.Http.Json;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Autorep.Web.Tests;

// Round-trips the Tester sync surface through the HTTP pipeline (authenticated as a Tester).
public class SyncControllerTests : IClassFixture<AuthedWebAppFactory>
{
    private readonly AuthedWebAppFactory _factory;
    public SyncControllerTests(AuthedWebAppFactory factory) => _factory = factory;

    private sealed record TestSummary(
        Guid ClientId, string FarmName, DateTimeOffset CreatedAt,
        DateTimeOffset? MarkedCompleteAt, ConfigSummary? Config);

    private sealed record ConfigSummary(string PlantType, int ClusterCount, bool HasAcr, bool VsdFitted);

    private sealed record PullResponse(DateTimeOffset Watermark, List<TestSummary> Tests);

    private static async Task<PullResponse> PullAsync(HttpClient client, DateTimeOffset? since = null)
    {
        var url = since is null
            ? "/api/sync/tests"
            : $"/api/sync/tests?since={Uri.EscapeDataString(since.Value.ToString("o"))}";
        var res = await client.GetFromJsonAsync<PullResponse>(url);
        res.Should().NotBeNull();
        return res!;
    }

    [Fact]
    public async Task Upload_then_list_round_trips_a_test_with_config()
    {
        var client = _factory.CreateClientAs(Roles.Tester, "tester-sync-1");
        var clientId = Guid.NewGuid();
        var payload = new
        {
            clientId,
            farmName = $"Sync Farm {clientId}",
            notes = (string?)null,
            markedCompleteAt = (DateTimeOffset?)null,
            createdAt = DateTimeOffset.UtcNow,
            config = new
            {
                plantType = "Rotary",
                clusterCount = 50,
                herdSize = (int?)null,
                lastBmcc = (string?)null,
                milklineSize = "100mm",
                flushingPulsationSystem = false,
                pulsatorModel = (string?)null,
                pulsatorCount = 0,
                clawModel = (string?)null,
                shellModel = (string?)null,
                linerModel = (string?)null,
                linerVented = false,
                numberOfVacuumPumps = 1,
                pumpLubrication = "LiquidRing",
                vsdFitted = true,
                isoPortsAvailable = true,
                hasPulsatorStopSystem = false,
                hasAcr = true,
                hasBailGates = false,
                hasMilkMeters = false,
                hasTeatSprayer = false,
                hasBackingGate = false,
                hasReleaserPump = false,
            },
        };

        var post = await client.PostAsJsonAsync("/api/sync/tests", payload);
        post.StatusCode.Should().Be(HttpStatusCode.Created);

        var list = (await PullAsync(client)).Tests;
        var mine = list.Single(t => t.ClientId == clientId);
        mine.FarmName.Should().Be(payload.farmName);
        mine.Config.Should().NotBeNull();
        mine.Config!.PlantType.Should().Be("Rotary");
        mine.Config.ClusterCount.Should().Be(50);
        mine.Config.HasAcr.Should().BeTrue();
        mine.Config.VsdFitted.Should().BeTrue();
    }

    [Fact]
    public async Task Upload_is_idempotent_by_client_id()
    {
        var client = _factory.CreateClientAs(Roles.Tester, "tester-sync-2");
        var clientId = Guid.NewGuid();
        var payload = new
        {
            clientId,
            farmName = "Idempotent Farm",
            notes = (string?)null,
            markedCompleteAt = (DateTimeOffset?)null,
            createdAt = DateTimeOffset.UtcNow,
            config = (object?)null,
        };

        var first = await client.PostAsJsonAsync("/api/sync/tests", payload);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await client.PostAsJsonAsync("/api/sync/tests", payload);
        second.StatusCode.Should().Be(HttpStatusCode.OK); // updated, not duplicated

        var list = (await PullAsync(client)).Tests;
        list.Count(t => t.ClientId == clientId).Should().Be(1);
    }

    [Fact]
    public async Task Sync_requires_the_tester_role()
    {
        var admin = _factory.CreateClientAs(Roles.SuperAdministrator, "admin-1");
        var res = await admin.GetAsync("/api/sync/tests");
        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // Seeds a tester as a member of a fresh Testing Company; returns the company id.
    private async Task<Guid> SeedTesterInCompanyAsync(string testerId, string companyName)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
        var company = new TestingCompany { Name = companyName };
        db.TestingCompanies.Add(company);
        db.Users.Add(new Tester { Id = testerId, UserName = testerId, TestingCompanyId = company.Id });
        await db.SaveChangesAsync();
        return company.Id;
    }

    private async Task<T> WithDbAsync<T>(Func<AutorepDbContext, Task<T>> f)
    {
        using var scope = _factory.Services.CreateScope();
        return await f(scope.ServiceProvider.GetRequiredService<AutorepDbContext>());
    }

    private static object UploadPayload(Guid clientId, string farmName,
        Guid? farmId = null, string? supply = null, string? milk = null) => new
    {
        clientId,
        farmName,
        farmId,
        farmSupplyNumber = supply,
        farmMilkCompanyName = milk,
        notes = (string?)null,
        markedCompleteAt = (DateTimeOffset?)null,
        createdAt = DateTimeOffset.UtcNow,
        config = (object?)null,
    };

    // The farm link must never cross company scope: another company's same-named farm is not
    // "the" farm — linking to it would grant this company visibility of that farm (and its
    // farmer contact details) through the test-derived scoping.
    [Fact]
    public async Task Upload_with_a_name_colliding_with_another_companys_farm_creates_a_separate_farm()
    {
        var farmName = "Colliding Farm " + Guid.NewGuid();
        var companyA = await WithDbAsync(async db =>
        {
            var company = new TestingCompany { Name = "Collide Co A" };
            db.TestingCompanies.Add(company);
            db.Farms.Add(new Farm { Name = farmName, CreatedByTestingCompanyId = company.Id, FarmerName = "A's farmer" });
            await db.SaveChangesAsync();
            return company.Id;
        });
        var companyB = await SeedTesterInCompanyAsync("tester-collide-b", "Collide Co B");

        var client = _factory.CreateClientAs(Roles.Tester, "tester-collide-b");
        var post = await client.PostAsJsonAsync("/api/sync/tests", UploadPayload(Guid.NewGuid(), farmName));
        post.StatusCode.Should().Be(HttpStatusCode.Created);

        var farms = await WithDbAsync(db => db.Farms.Where(f => f.Name == farmName).ToListAsync());
        farms.Should().HaveCount(2, "company B's sync must get its own farm, not company A's");
        var linkedFarmId = await WithDbAsync(db => db.MachineTests
            .Where(t => t.TesterId == "tester-collide-b").Select(t => t.FarmId).SingleAsync());
        farms.Single(f => f.Id == linkedFarmId).CreatedByTestingCompanyId.Should().Be(companyB);
        farms.Single(f => f.Id != linkedFarmId).CreatedByTestingCompanyId.Should().Be(companyA);
    }

    [Fact]
    public async Task Upload_links_by_farm_id_when_the_farm_is_in_the_testers_company_scope()
    {
        var companyId = await SeedTesterInCompanyAsync("tester-byid-1", "ById Co");
        var farmId = await WithDbAsync(async db =>
        {
            var farm = new Farm { Name = "ById Farm " + Guid.NewGuid(), CreatedByTestingCompanyId = companyId };
            db.Farms.Add(farm);
            await db.SaveChangesAsync();
            return farm.Id;
        });

        var client = _factory.CreateClientAs(Roles.Tester, "tester-byid-1");
        // The device may carry a stale/renamed farm name — the in-scope id wins.
        var post = await client.PostAsJsonAsync("/api/sync/tests",
            UploadPayload(Guid.NewGuid(), "Renamed On Device " + Guid.NewGuid(), farmId: farmId));
        post.StatusCode.Should().Be(HttpStatusCode.Created);

        var linkedFarmId = await WithDbAsync(db => db.MachineTests
            .Where(t => t.TesterId == "tester-byid-1").Select(t => t.FarmId).SingleAsync());
        linkedFarmId.Should().Be(farmId);
    }

    [Fact]
    public async Task Upload_with_a_foreign_farm_id_does_not_link_that_farm()
    {
        var foreignFarmId = await WithDbAsync(async db =>
        {
            var otherCompany = new TestingCompany { Name = "Foreign Co" };
            db.TestingCompanies.Add(otherCompany);
            var farm = new Farm { Name = "Foreign Farm " + Guid.NewGuid(), CreatedByTestingCompanyId = otherCompany.Id };
            db.Farms.Add(farm);
            await db.SaveChangesAsync();
            return farm.Id;
        });
        var companyId = await SeedTesterInCompanyAsync("tester-foreign-1", "Foreign-Probe Co");

        var client = _factory.CreateClientAs(Roles.Tester, "tester-foreign-1");
        var farmName = "Probe Farm " + Guid.NewGuid();
        var post = await client.PostAsJsonAsync("/api/sync/tests",
            UploadPayload(Guid.NewGuid(), farmName, farmId: foreignFarmId));
        post.StatusCode.Should().Be(HttpStatusCode.Created);

        var test = await WithDbAsync(db => db.MachineTests
            .Include(t => t.Farm).SingleAsync(t => t.TesterId == "tester-foreign-1"));
        test.FarmId.Should().NotBe(foreignFarmId, "a crafted/foreign farm id must fall through to create");
        test.Farm!.Name.Should().Be(farmName);
        test.Farm.CreatedByTestingCompanyId.Should().Be(companyId);
    }

    [Fact]
    public async Task Upload_without_farm_id_matches_an_own_company_farm_on_name_supply_and_milk_company()
    {
        var companyId = await SeedTesterInCompanyAsync("tester-identity-1", "Identity Co");
        var farmName = "Identity Farm " + Guid.NewGuid();
        var milkName = "Milk Co " + Guid.NewGuid();
        var farmId = await WithDbAsync(async db =>
        {
            var milk = new MilkSupplyCompany { Name = milkName };
            db.MilkSupplyCompanies.Add(milk);
            var farm = new Farm
            {
                Name = farmName, SupplyNumber = "11111",
                MilkSupplyCompanyId = milk.Id, CreatedByTestingCompanyId = companyId,
            };
            db.Farms.Add(farm);
            await db.SaveChangesAsync();
            return farm.Id;
        });

        var client = _factory.CreateClientAs(Roles.Tester, "tester-identity-1");
        var post = await client.PostAsJsonAsync("/api/sync/tests",
            UploadPayload(Guid.NewGuid(), farmName, supply: "11111", milk: milkName));
        post.StatusCode.Should().Be(HttpStatusCode.Created);

        var linkedFarmId = await WithDbAsync(db => db.MachineTests
            .Where(t => t.TesterId == "tester-identity-1").Select(t => t.FarmId).SingleAsync());
        linkedFarmId.Should().Be(farmId);
        (await WithDbAsync(db => db.Farms.CountAsync(f => f.Name == farmName))).Should().Be(1);
    }

    [Fact]
    public async Task Upload_with_a_different_supply_number_creates_a_new_farm_even_within_the_company()
    {
        var companyId = await SeedTesterInCompanyAsync("tester-supply-1", "Supply Co");
        var farmName = "Supply Farm " + Guid.NewGuid();
        await WithDbAsync(async db =>
        {
            db.Farms.Add(new Farm { Name = farmName, SupplyNumber = "11111", CreatedByTestingCompanyId = companyId });
            await db.SaveChangesAsync();
            return 0;
        });

        var client = _factory.CreateClientAs(Roles.Tester, "tester-supply-1");
        var post = await client.PostAsJsonAsync("/api/sync/tests",
            UploadPayload(Guid.NewGuid(), farmName, supply: "22222"));
        post.StatusCode.Should().Be(HttpStatusCode.Created);

        var farms = await WithDbAsync(db => db.Farms.Where(f => f.Name == farmName).ToListAsync());
        farms.Should().HaveCount(2, "a different supply number is a different farm identity");
        farms.Select(f => f.SupplyNumber).Should().BeEquivalentTo(new[] { "11111", "22222" });
    }

    [Fact]
    public async Task Upload_with_a_different_milk_company_creates_a_new_farm_even_within_the_company()
    {
        var companyId = await SeedTesterInCompanyAsync("tester-milk-1", "Milk-Split Co");
        var farmName = "Milk Farm " + Guid.NewGuid();
        var fonterra = "Fonterra " + Guid.NewGuid();
        var synlait = "Synlait " + Guid.NewGuid();
        await WithDbAsync(async db =>
        {
            var milk = new MilkSupplyCompany { Name = fonterra };
            db.MilkSupplyCompanies.Add(milk);
            db.MilkSupplyCompanies.Add(new MilkSupplyCompany { Name = synlait });
            db.Farms.Add(new Farm
            {
                Name = farmName, SupplyNumber = "333",
                MilkSupplyCompanyId = milk.Id, CreatedByTestingCompanyId = companyId,
            });
            await db.SaveChangesAsync();
            return 0;
        });

        var client = _factory.CreateClientAs(Roles.Tester, "tester-milk-1");
        var post = await client.PostAsJsonAsync("/api/sync/tests",
            UploadPayload(Guid.NewGuid(), farmName, supply: "333", milk: synlait));
        post.StatusCode.Should().Be(HttpStatusCode.Created);

        var farms = await WithDbAsync(db => db.Farms.Include(f => f.MilkSupplyCompany)
            .Where(f => f.Name == farmName).ToListAsync());
        farms.Should().HaveCount(2, "a different milk processor is a different farm identity");
        farms.Select(f => f.MilkSupplyCompany!.Name).Should().BeEquivalentTo(new[] { fonterra, synlait });
    }

    // The migrated-farm shape: legacy farms carry test history but a null
    // CreatedByTestingCompanyId, so the by-id link must honour the tests leg of the scope.
    [Fact]
    public async Task Upload_links_by_farm_id_when_the_farm_is_in_scope_via_tests_only()
    {
        await SeedTesterInCompanyAsync("tester-testleg-1", "Test-Leg Co");
        var farmId = await WithDbAsync(async db =>
        {
            var farm = new Farm { Name = "Legacy Farm " + Guid.NewGuid() }; // no CreatedBy — migrated shape
            db.Farms.Add(farm);
            db.MachineTests.Add(new MachineTest
            {
                TesterId = "tester-testleg-1", FarmId = farm.Id, CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
            return farm.Id;
        });

        var client = _factory.CreateClientAs(Roles.Tester, "tester-testleg-1");
        var post = await client.PostAsJsonAsync("/api/sync/tests",
            UploadPayload(Guid.NewGuid(), "Whatever Name", farmId: farmId));
        post.StatusCode.Should().Be(HttpStatusCode.Created);

        var farmIds = await WithDbAsync(db => db.MachineTests
            .Where(t => t.TesterId == "tester-testleg-1").Select(t => t.FarmId).ToListAsync());
        farmIds.Should().OnlyContain(id => id == farmId, "the farm is in scope via the company's test history");
    }

    // A tester with no Testing Company (not creatable via admin UI, but a supported fallback in
    // FarmScope) re-matches their own previously synced farm instead of duplicating it per push.
    [Fact]
    public async Task Upload_by_a_companyless_tester_rematches_their_own_farm_across_pushes()
    {
        var farmName = "Unaffiliated Farm " + Guid.NewGuid();
        var client = _factory.CreateClientAs(Roles.Tester, "tester-nocompany-1"); // no Users row seeded

        (await client.PostAsJsonAsync("/api/sync/tests", UploadPayload(Guid.NewGuid(), farmName)))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await client.PostAsJsonAsync("/api/sync/tests", UploadPayload(Guid.NewGuid(), farmName)))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        (await WithDbAsync(db => db.Farms.CountAsync(f => f.Name == farmName)))
            .Should().Be(1, "the second push must match the farm the first push created");
    }

    [Fact]
    public async Task Upload_reusing_another_testers_client_id_does_not_overwrite_their_test()
    {
        var clientId = Guid.NewGuid();

        var a = _factory.CreateClientAs(Roles.Tester, "tester-idor-a");
        var aPayload = new
        {
            clientId, farmName = "A Farm", notes = "A-notes",
            markedCompleteAt = (DateTimeOffset?)null, createdAt = DateTimeOffset.UtcNow, config = (object?)null,
        };
        (await a.PostAsJsonAsync("/api/sync/tests", aPayload)).StatusCode.Should().Be(HttpStatusCode.Created);

        // Tester B reuses A's ClientId — must create B's own row, never touch A's.
        var b = _factory.CreateClientAs(Roles.Tester, "tester-idor-b");
        var bPayload = new
        {
            clientId, farmName = "B Farm", notes = "B-notes",
            markedCompleteAt = (DateTimeOffset?)null, createdAt = DateTimeOffset.UtcNow, config = (object?)null,
        };
        (await b.PostAsJsonAsync("/api/sync/tests", bPayload)).StatusCode.Should().Be(HttpStatusCode.Created);

        var aList = (await PullAsync(a)).Tests;
        aList.Single(t => t.ClientId == clientId).FarmName.Should().Be("A Farm"); // unchanged

        var bList = (await PullAsync(b)).Tests;
        bList.Single(t => t.ClientId == clientId).FarmName.Should().Be("B Farm");
    }

    // Seeds a Company Administrator with real role rows so the review notifier can find them.
    private async Task SeedCompanyAdminAsync(Guid companyId, string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == Roles.CompanyAdministrator);
        if (role is null)
        {
            role = new Microsoft.AspNetCore.Identity.IdentityRole
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
        db.UserRoles.Add(new Microsoft.AspNetCore.Identity.IdentityUserRole<string>
        {
            RoleId = role.Id, UserId = admin.Id,
        });
        await db.SaveChangesAsync();
    }

    // A farm minted by an offline sync push goes under review (matching the New-test modal)
    // and the company's administrators are emailed — but only on the push that created it.
    [Fact]
    public async Task Upload_creating_a_farm_flags_it_for_review_and_notifies_once()
    {
        var companyId = await SeedTesterInCompanyAsync("tester-review-1", "Review Co");
        var adminEmail = $"review-admin-{Guid.NewGuid()}@example.test";
        await SeedCompanyAdminAsync(companyId, adminEmail);
        var farmName = "Review Farm " + Guid.NewGuid();

        var client = _factory.CreateClientAs(Roles.Tester, "tester-review-1");
        (await client.PostAsJsonAsync("/api/sync/tests", UploadPayload(Guid.NewGuid(), farmName)))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var farm = await WithDbAsync(db => db.Farms.SingleAsync(f => f.Name == farmName));
        farm.PendingReviewSince.Should().NotBeNull();
        farm.CreatedByTesterId.Should().Be("tester-review-1");

        var mail = _factory.Emails.All.Should()
            .ContainSingle(m => m.Subject.Contains(farmName)).Which;
        mail.Email.Should().Be(adminEmail);
        mail.HtmlMessage.Should().Contain($"/Admin/Farms/Edit/{farm.Id}");

        // A second push against the same farm identity links the existing farm: no re-flag
        // of an approved farm, no second notification.
        await WithDbAsync(async db =>
        {
            var f = await db.Farms.SingleAsync(x => x.Name == farmName);
            f.PendingReviewSince = null; // admin approved in the meantime
            await db.SaveChangesAsync();
            return 0;
        });
        (await client.PostAsJsonAsync("/api/sync/tests", UploadPayload(Guid.NewGuid(), farmName)))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        (await WithDbAsync(db => db.Farms.SingleAsync(f => f.Name == farmName)))
            .PendingReviewSince.Should().BeNull("linking an existing farm must not re-flag it");
        _factory.Emails.All.Should().ContainSingle(m => m.Subject.Contains(farmName));
    }

    // A tester who also holds an administrator role reviews their own farms by definition.
    [Fact]
    public async Task Upload_by_an_administrator_tester_creates_the_farm_unflagged()
    {
        await SeedTesterInCompanyAsync("tester-admin-review-1", "Admin-Review Co");
        var farmName = "Admin Review Farm " + Guid.NewGuid();

        var client = _factory.CreateClientAs(
            $"{Roles.Tester},{Roles.CompanyAdministrator}", "tester-admin-review-1");
        (await client.PostAsJsonAsync("/api/sync/tests", UploadPayload(Guid.NewGuid(), farmName)))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var farm = await WithDbAsync(db => db.Farms.SingleAsync(f => f.Name == farmName));
        farm.PendingReviewSince.Should().BeNull();
        _factory.Emails.All.Should().NotContain(m => m.Subject.Contains(farmName));
    }

    [Fact]
    public async Task Delta_pull_filters_by_watermark_and_updates_reenter_the_stream()
    {
        var client = _factory.CreateClientAs(Roles.Tester, "tester-delta-1");

        var first = Guid.NewGuid();
        (await client.PostAsJsonAsync("/api/sync/tests", new
        {
            clientId = first, farmName = "Delta Farm 1", notes = (string?)null,
            markedCompleteAt = (DateTimeOffset?)null, createdAt = DateTimeOffset.UtcNow, config = (object?)null,
        })).StatusCode.Should().Be(HttpStatusCode.Created);

        // Full pull: sees the first test; the watermark it hands back is LAGGED behind now (the
        // anti-race overlap), so a pull from that watermark re-delivers the fresh test.
        var full = await PullAsync(client);
        full.Tests.Should().Contain(t => t.ClientId == first);
        full.Watermark.Should().BeBefore(DateTimeOffset.UtcNow.AddSeconds(-60));
        (await PullAsync(client, full.Watermark)).Tests.Should().Contain(t => t.ClientId == first);

        // From a point after the write, the delta is empty…
        var afterFirst = DateTimeOffset.UtcNow;
        (await PullAsync(client, afterFirst)).Tests.Should().BeEmpty();

        var second = Guid.NewGuid();
        (await client.PostAsJsonAsync("/api/sync/tests", new
        {
            clientId = second, farmName = "Delta Farm 2", notes = (string?)null,
            markedCompleteAt = (DateTimeOffset?)null, createdAt = DateTimeOffset.UtcNow, config = (object?)null,
        })).StatusCode.Should().Be(HttpStatusCode.Created);

        // …and only the test written after that point comes down.
        var delta = await PullAsync(client, afterFirst);
        delta.Tests.Should().ContainSingle(t => t.ClientId == second);
        delta.Tests.Should().NotContain(t => t.ClientId == first);

        // An UPDATE re-enters the delta stream (server re-stamps UpdatedAt on upsert).
        var beforeUpdate = DateTimeOffset.UtcNow;
        (await client.PostAsJsonAsync("/api/sync/tests", new
        {
            clientId = first, farmName = "Delta Farm 1", notes = "edited later",
            markedCompleteAt = (DateTimeOffset?)null, createdAt = DateTimeOffset.UtcNow, config = (object?)null,
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        (await PullAsync(client, beforeUpdate)).Tests
            .Should().ContainSingle(t => t.ClientId == first);
    }
}
