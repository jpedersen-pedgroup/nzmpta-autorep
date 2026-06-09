using System.Net;
using System.Net.Http.Json;
using Autorep.Web.Domain;
using FluentAssertions;

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

        var list = await client.GetFromJsonAsync<List<TestSummary>>("/api/sync/tests");
        list.Should().NotBeNull();
        var mine = list!.Single(t => t.ClientId == clientId);
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

        var list = await client.GetFromJsonAsync<List<TestSummary>>("/api/sync/tests");
        list!.Count(t => t.ClientId == clientId).Should().Be(1);
    }

    [Fact]
    public async Task Sync_requires_the_tester_role()
    {
        var admin = _factory.CreateClientAs(Roles.SuperAdministrator, "admin-1");
        var res = await admin.GetAsync("/api/sync/tests");
        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
