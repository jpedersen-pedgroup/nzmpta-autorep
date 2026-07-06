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
