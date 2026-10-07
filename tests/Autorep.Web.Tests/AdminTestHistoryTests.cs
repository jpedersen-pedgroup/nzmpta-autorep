using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Autorep.Web.Tests.TestPayloads;

namespace Autorep.Web.Tests;

// PRD stories 68–69: the admin viewer's audit panel reads /api/admin/tests/{id}/history — every
// version of the test with who made it and its own amendment record, the sync conflicts between
// versions, and administrators' edits, deletions and restores. Scoped like the read.
public class AdminTestHistoryTests : IClassFixture<AuthedWebAppFactory>
{
    private readonly AuthedWebAppFactory _factory;
    public AdminTestHistoryTests(AuthedWebAppFactory factory) => _factory = factory;

    private IServiceProvider Services => _factory.Services;

    private async Task<T> WithDbAsync<T>(Func<AutorepDbContext, Task<T>> f)
    {
        using var scope = Services.CreateScope();
        return await f(scope.ServiceProvider.GetRequiredService<AutorepDbContext>());
    }

    private sealed record Arranged(Guid CompanyId, Guid FarmId, MachineTest V1, JsonObject Payload, HttpClient Admin);

    /// <summary>A tester's completed test, their company's administrator, and a Super-Administrator.</summary>
    private async Task<Arranged> ArrangeAsync(string tag)
    {
        var (companyId, farmId) = await SeedCompanyAsync(Services, tag);
        await SeedUserAsync(Services, $"{tag}-tester", companyId, "Ellie Tester");
        await SeedUserAsync(Services, $"{tag}-coadmin", companyId, "Cora Companyadmin");
        await SeedUserAsync(Services, $"{tag}-admin", null, "Sam Superadmin");
        var clientId = Guid.NewGuid();
        var payload = Original(clientId);
        var v1 = await SeedTestAsync(Services, $"{tag}-tester", farmId, companyId, payload, clientId: clientId);
        return new Arranged(companyId, farmId, v1, payload, _factory.CreateClientAs(Roles.SuperAdministrator, $"{tag}-admin"));
    }

    private static async Task<Guid> SaveVersionAsync(HttpClient admin, Guid baseId, JsonObject edited, string reason)
    {
        var res = await admin.PostAsJsonAsync($"/api/admin/tests/{baseId}/versions", new { payloadJson = edited.ToJsonString(), reason });
        res.StatusCode.Should().Be(HttpStatusCode.Created, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<Guid>();
    }

    private static async Task<JsonObject> HistoryAsync(HttpClient client, Guid id)
    {
        var res = await client.GetAsync($"/api/admin/tests/{id}/history");
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        res.Headers.CacheControl!.NoStore.Should().BeTrue();
        return (await res.Content.ReadFromJsonAsync<JsonObject>())!;
    }

    [Fact]
    public async Task Lists_every_version_with_who_made_it_its_own_amendment_and_its_attestations()
    {
        var a = await ArrangeAsync("hist-versions");
        var v2Id = await SaveVersionAsync(a.Admin, a.V1.Id,
            Edited(a.Payload, p => p["notes"] = "Corrected comment"), "Comment was for another farm");

        // Asked about either version, the history is the same: the whole test.
        var fromV1 = await HistoryAsync(a.Admin, a.V1.Id);
        var history = await HistoryAsync(a.Admin, v2Id);
        fromV1.ToJsonString().Should().Be(history.ToJsonString());

        var versions = history["versions"]!.AsArray();
        versions.Should().HaveCount(2);
        var v1 = versions[0]!;
        v1["id"]!.GetValue<Guid>().Should().Be(a.V1.Id);
        v1["version"]!.GetValue<int>().Should().Be(1);
        v1["authorKind"]!.GetValue<string>().Should().Be("tester");
        v1["author"]!.GetValue<string>().Should().Be("Ellie Tester");
        v1["isCurrent"]!.GetValue<bool>().Should().BeFalse();
        v1["amendment"].Should().BeNull("an original has no amendment record of its own");
        v1["attestations"]!.AsArray().Should().HaveCount(2);

        var v2 = versions[1]!;
        v2["id"]!.GetValue<Guid>().Should().Be(v2Id);
        v2["version"]!.GetValue<int>().Should().Be(2);
        v2["authorKind"]!.GetValue<string>().Should().Be("admin");
        v2["author"]!.GetValue<string>().Should().Be("Sam Superadmin");
        v2["isCurrent"]!.GetValue<bool>().Should().BeTrue();
        v2["amendment"]!["version"]!.GetValue<int>().Should().Be(2);
        v2["amendment"]!["reason"]!.GetValue<string>().Should().Be("Comment was for another farm");
        v2["amendment"]!["amendedByRole"]!.GetValue<string>().Should().Be("Super Administrator");
        v2["amendment"]!["changes"]!.AsArray().Should().NotBeEmpty();
        v2["attestations"]!.AsArray().Should().HaveCount(2, "the tester's attestations stand on an administrator's version");

        var events = history["events"]!.AsArray();
        events.Should().ContainSingle();
        events[0]!["operation"]!.GetValue<string>().Should().Be("AdminVersionCreated");
        events[0]!["actor"]!.GetValue<string>().Should().Be("Sam Superadmin");
        events[0]!["version"]!.GetValue<int>().Should().Be(2);
        events[0]!["reason"]!.GetValue<string>().Should().Be("Comment was for another farm");
        history["conflicts"]!.AsArray().Should().BeEmpty();
    }

    [Fact]
    public async Task Shows_a_collision_by_version_id_and_what_it_was_combined_into()
    {
        var a = await ArrangeAsync("hist-conflict");
        var tester = "hist-conflict-tester";
        // Two version 2s made from version 1 — an administrator's and the tester's — combined as version 3.
        var adminV2 = await SeedTestAsync(Services, tester, a.FarmId, a.CompanyId, Original(Guid.NewGuid()), version: 2,
            supersedes: a.V1.ClientId, root: a.V1.ClientId);
        var testerV2 = await SeedTestAsync(Services, tester, a.FarmId, a.CompanyId, Original(Guid.NewGuid()), version: 2,
            supersedes: a.V1.ClientId, root: a.V1.ClientId);
        var merged = await SeedTestAsync(Services, tester, a.FarmId, a.CompanyId, Original(Guid.NewGuid()), version: 3,
            supersedes: adminV2.ClientId, root: a.V1.ClientId);
        await WithDbAsync(async db =>
        {
            (await db.MachineTests.SingleAsync(t => t.Id == adminV2.Id)).AuthorId = "hist-conflict-admin";
            (await db.MachineTests.SingleAsync(t => t.Id == merged.Id)).MergedFromClientId = testerV2.ClientId;
            db.SyncConflicts.Add(new SyncConflict
            {
                TesterId = tester, RootClientId = a.V1.ClientId!.Value,
                BaseClientId = a.V1.ClientId!.Value, HeadClientId = adminV2.ClientId!.Value, IncomingClientId = testerV2.ClientId!.Value,
                MergedClientId = merged.ClientId, Status = SyncConflictStatus.Merged, DetectedOn = SyncConflictSource.Push,
                DetectedAt = DateTimeOffset.UtcNow.AddMinutes(-5), ResolvedAt = DateTimeOffset.UtcNow,
                OverlappingFieldsJson = JsonSerializer.Serialize(new[] { "notes" }),
            });
            return await db.SaveChangesAsync();
        });

        var history = await HistoryAsync(a.Admin, a.V1.Id);

        var kinds = history["versions"]!.AsArray().Select(v => (v!["version"]!.GetValue<int>(), v["authorKind"]!.GetValue<string>()));
        kinds.Should().Equal((1, "tester"), (2, "admin"), (2, "tester"), (3, "merge"));
        var current = history["versions"]!.AsArray().Single(v => v!["isCurrent"]!.GetValue<bool>())!;
        current["id"]!.GetValue<Guid>().Should().Be(merged.Id);

        var conflict = history["conflicts"]!.AsArray().Single()!;
        conflict["status"]!.GetValue<string>().Should().Be("Merged");
        conflict["detectedOn"]!.GetValue<string>().Should().Be("Push");
        conflict["baseId"]!.GetValue<Guid>().Should().Be(a.V1.Id);
        conflict["headId"]!.GetValue<Guid>().Should().Be(adminV2.Id);
        conflict["incomingId"]!.GetValue<Guid>().Should().Be(testerV2.Id);
        conflict["mergedId"]!.GetValue<Guid>().Should().Be(merged.Id);
        conflict["overlappingFields"]!.AsArray().Select(f => f!.GetValue<string>()).Should().Equal("notes");

        // The viewer tells administrators there's something to look at; a tester's view doesn't carry it.
        var view = (await a.Admin.GetFromJsonAsync<JsonObject>($"/api/tests/{merged.Id}"))!;
        view["conflictCount"]!.GetValue<int>().Should().Be(1);
        var asTester = _factory.CreateClientAs(Roles.Tester, tester);
        (await asTester.GetFromJsonAsync<JsonObject>($"/api/tests/{merged.Id}"))!["conflictCount"]!.GetValue<int>().Should().Be(0);
    }

    [Fact]
    public async Task Lists_a_deletion_and_a_restore_with_the_reason()
    {
        var a = await ArrangeAsync("hist-delete");
        (await a.Admin.PostAsJsonAsync($"/api/admin/tests/{a.V1.Id}/delete", new { reason = "Duplicate of another test" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await a.Admin.PostAsync($"/api/admin/tests/{a.V1.Id}/restore", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var events = (await HistoryAsync(a.Admin, a.V1.Id))["events"]!.AsArray();

        events.Select(e => e!["operation"]!.GetValue<string>()).Should().Equal("SoftDeleted", "Restored");
        events[0]!["reason"]!.GetValue<string>().Should().Be("Duplicate of another test");
        events[0]!["actor"]!.GetValue<string>().Should().Be("Sam Superadmin");
        events[1]!["reason"].Should().BeNull();
    }

    [Fact]
    public async Task Is_scoped_like_the_read()
    {
        var a = await ArrangeAsync("hist-scope");
        var (otherCompany, _) = await SeedCompanyAsync(Services, "hist-scope-other");
        await SeedUserAsync(Services, "hist-scope-otheradmin", otherCompany);
        var ownAdmin = _factory.CreateClientAs(Roles.CompanyAdministrator, "hist-scope-coadmin");
        var otherAdmin = _factory.CreateClientAs(Roles.CompanyAdministrator, "hist-scope-otheradmin");
        var tester = _factory.CreateClientAs(Roles.Tester, "hist-scope-tester");

        (await ownAdmin.GetAsync($"/api/admin/tests/{a.V1.Id}/history")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await otherAdmin.GetAsync($"/api/admin/tests/{a.V1.Id}/history")).StatusCode
            .Should().Be(HttpStatusCode.NotFound, "another company's test isn't disclosed");
        (await tester.GetAsync($"/api/admin/tests/{a.V1.Id}/history")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await a.Admin.GetAsync($"/api/admin/tests/{Guid.NewGuid()}/history")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
