using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Autorep.Web.Tests.TestPayloads;

namespace Autorep.Web.Tests;

// PRD story 70: a Super-Administrator soft-deletes a test with a mandatory reason. The test — every
// version of it — leaves every list, Upcoming and the tester's device, but stays on record. Devices
// learn of it through tombstones in the pull, which keeps deleted rows in its result set so the
// offset cursor can never skip a row.
public class SoftDeleteTests : IClassFixture<AuthedWebAppFactory>
{
    private readonly AuthedWebAppFactory _factory;
    public SoftDeleteTests(AuthedWebAppFactory factory) => _factory = factory;

    private IServiceProvider Services => _factory.Services;

    private async Task<T> WithDbAsync<T>(Func<AutorepDbContext, Task<T>> f)
    {
        using var scope = Services.CreateScope();
        return await f(scope.ServiceProvider.GetRequiredService<AutorepDbContext>());
    }

    private sealed record Arranged(string TesterId, Guid CompanyId, Guid FarmId, MachineTest V1, MachineTest V2, HttpClient Admin);

    /// <summary>A tester's completed test with a second version, and a Super-Administrator.</summary>
    private async Task<Arranged> ArrangeAsync(string tag)
    {
        var (companyId, farmId) = await SeedCompanyAsync(Services, tag);
        var testerId = $"{tag}-tester";
        await SeedUserAsync(Services, testerId, companyId);
        await SeedUserAsync(Services, $"{tag}-coadmin", companyId);
        await SeedUserAsync(Services, $"{tag}-admin", null, "Sam Superadmin");
        var v1Id = Guid.NewGuid();
        var v1 = await SeedTestAsync(Services, testerId, farmId, companyId, Original(v1Id), clientId: v1Id);
        var v2Id = Guid.NewGuid();
        var v2Payload = Original(v2Id);
        v2Payload["version"] = 2;
        v2Payload["supersedesId"] = v1Id.ToString();
        var v2 = await SeedTestAsync(Services, testerId, farmId, companyId, v2Payload, version: 2, clientId: v2Id, supersedes: v1Id, root: v1Id);
        return new Arranged(testerId, companyId, farmId, v1, v2, _factory.CreateClientAs(Roles.SuperAdministrator, $"{tag}-admin"));
    }

    private static Task<HttpResponseMessage> DeleteAsync(HttpClient client, Guid id, string? reason = "Entered against the wrong farm") =>
        client.PostAsJsonAsync($"/api/admin/tests/{id}/delete", new { reason });

    [Fact]
    public async Task A_super_admin_deletes_every_version_of_a_test_keeping_who_when_and_why()
    {
        var a = await ArrangeAsync("sd-all");
        var before = DateTimeOffset.UtcNow;

        var res = await DeleteAsync(a.Admin, a.V1.Id);

        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var rows = await WithDbAsync(db => db.MachineTests.Where(t => t.TesterId == a.TesterId).ToListAsync());
        rows.Should().HaveCount(2).And.OnlyContain(t => t.IsDeleted
            && t.DeletedById == "sd-all-admin"
            && t.DeletedReason == "Entered against the wrong farm"
            && t.DeletedAt >= before
            && t.UpdatedAt >= before);
        var audit = await WithDbAsync(db => db.AuditEntries.SingleAsync(e => e.Operation == "SoftDeleted" && e.EntityKey == a.V1.Id.ToString()));
        audit.Actor.Should().Be("sd-all-admin");
        audit.AfterJson.Should().Contain("Entered against the wrong farm");
    }

    [Fact]
    public async Task A_reason_is_required()
    {
        var a = await ArrangeAsync("sd-reason");

        (await DeleteAsync(a.Admin, a.V2.Id, reason: " ")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await WithDbAsync(db => db.MachineTests.AnyAsync(t => t.TesterId == a.TesterId && t.IsDeleted))).Should().BeFalse();
    }

    [Fact]
    public async Task Only_a_super_admin_can_delete_or_restore()
    {
        var a = await ArrangeAsync("sd-roles");
        var companyAdmin = _factory.CreateClientAs(Roles.CompanyAdministrator, "sd-roles-coadmin");
        var tester = _factory.CreateClientAs(Roles.Tester, a.TesterId);

        (await DeleteAsync(companyAdmin, a.V2.Id)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await DeleteAsync(tester, a.V2.Id)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await companyAdmin.PostAsync($"/api/admin/tests/{a.V2.Id}/restore", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_deleted_test_leaves_the_company_list_upcoming_and_the_testers_view()
    {
        var a = await ArrangeAsync("sd-hidden");
        var tester = _factory.CreateClientAs(Roles.Tester, a.TesterId);
        var companyAdmin = _factory.CreateClientAs(Roles.CompanyAdministrator, "sd-hidden-coadmin");
        await WithDbAsync(async db =>
        {
            (await db.MachineTests.SingleAsync(t => t.Id == a.V2.Id)).NextTestDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10);
            return await db.SaveChangesAsync();
        });
        var farmName = await WithDbAsync(db => db.Farms.Where(f => f.Id == a.FarmId).Select(f => f.Name).SingleAsync());
        (await companyAdmin.GetStringAsync("/Admin/Tests/Upcoming")).Should().Contain(farmName, "due before the deletion");

        (await DeleteAsync(a.Admin, a.V2.Id)).StatusCode.Should().Be(HttpStatusCode.OK);

        var list = JsonNode.Parse(await tester.GetStringAsync("/api/tests"))!;
        list["items"]!.AsArray().Should().BeEmpty();
        (await tester.GetAsync($"/api/tests/{a.V2.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await tester.GetAsync($"/api/sync/tests/{a.V2.ClientId}/pulsation-pdf")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await companyAdmin.GetStringAsync("/Admin/Tests/Upcoming")).Should().NotContain(farmName);
        (await companyAdmin.GetStringAsync("/Admin/Tests")).Should().NotContain(farmName);
        var withDeleted = await companyAdmin.GetStringAsync("/Admin/Tests?showDeleted=true");
        withDeleted.Should().Contain(farmName).And.Contain("Deleted");
    }

    private sealed record View(string? EditBlocked, string? EditScope, JsonObject? Deletion, bool CanDelete, bool CanRestore);

    [Fact]
    public async Task Administrators_still_open_a_deleted_test_and_see_why()
    {
        var a = await ArrangeAsync("sd-view");
        (await DeleteAsync(a.Admin, a.V2.Id)).StatusCode.Should().Be(HttpStatusCode.OK);
        var companyAdmin = _factory.CreateClientAs(Roles.CompanyAdministrator, "sd-view-coadmin");

        var asSuper = (await a.Admin.GetFromJsonAsync<View>($"/api/tests/{a.V2.Id}"))!;
        var asCompany = (await companyAdmin.GetFromJsonAsync<View>($"/api/tests/{a.V2.Id}"))!;

        asSuper.EditBlocked.Should().Be("deleted");
        asSuper.EditScope.Should().BeNull();
        asSuper.Deletion!["reason"]!.GetValue<string>().Should().Be("Entered against the wrong farm");
        asSuper.Deletion["by"]!.GetValue<string>().Should().Be("Sam Superadmin");
        asSuper.CanRestore.Should().BeTrue();
        asSuper.CanDelete.Should().BeFalse();
        asCompany.Deletion.Should().NotBeNull();
        asCompany.CanRestore.Should().BeFalse();
    }

    [Fact]
    public async Task A_deleted_test_cant_be_edited()
    {
        var a = await ArrangeAsync("sd-edit");
        (await DeleteAsync(a.Admin, a.V2.Id)).StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = JsonNode.Parse(a.V2.PayloadJson!)!.AsObject();

        var res = await a.Admin.PostAsJsonAsync($"/api/admin/tests/{a.V2.Id}/versions", new
        {
            payloadJson = Edited(payload, p => p["notes"] = "x", version: 3).ToJsonString(),
            reason = "x",
        });

        res.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await res.Content.ReadFromJsonAsync<JsonObject>())!["error"]!.GetValue<string>().Should().Be("deleted");
    }

    // ---- The device learns of it ---------------------------------------------------------

    [Fact]
    public async Task The_next_delta_pull_delivers_every_version_as_a_tombstone()
    {
        var a = await ArrangeAsync("sd-tomb");
        var tester = _factory.CreateClientAs(Roles.Tester, a.TesterId);
        var watermark = JsonNode.Parse(await tester.GetStringAsync("/api/sync/tests"))!["watermark"]!.GetValue<DateTimeOffset>();

        (await DeleteAsync(a.Admin, a.V1.Id)).StatusCode.Should().Be(HttpStatusCode.OK);

        var since = Uri.EscapeDataString(watermark.ToString("o"));
        var tests = JsonNode.Parse(await tester.GetStringAsync($"/api/sync/tests?since={since}"))!["tests"]!.AsArray();
        var tombstones = tests.Where(t => t!["clientId"]!.GetValue<Guid>() == a.V1.ClientId || t["clientId"]!.GetValue<Guid>() == a.V2.ClientId).ToList();
        tombstones.Should().HaveCount(2);
        tombstones.Should().OnlyContain(t => t!["deleted"]!.GetValue<bool>()
            && t["deletedReason"]!.GetValue<string>() == "Entered against the wrong farm");
        // The payload still comes, so a device older than tombstones doesn't blank its copy.
        tombstones.Should().OnlyContain(t => t!["payloadJson"] != null);
    }

    // The offset cursor is safe only while rows never leave the pull's result set. A test deleted
    // between two pages must still be delivered, and nothing after it may be skipped.
    [Fact]
    public async Task A_test_deleted_in_the_middle_of_a_paged_pull_is_still_delivered_and_nothing_is_skipped()
    {
        var (companyId, farmId) = await SeedCompanyAsync(Services, "sd-paging");
        await SeedUserAsync(Services, "sd-paging-tester", companyId);
        await SeedUserAsync(Services, "sd-paging-admin", null);
        var admin = _factory.CreateClientAs(Roles.SuperAdministrator, "sd-paging-admin");
        var tester = _factory.CreateClientAs(Roles.Tester, "sd-paging-tester");
        var ids = new List<(Guid Id, Guid ClientId)>();
        for (var i = 0; i < 7; i++)
        {
            var clientId = Guid.NewGuid();
            var row = await SeedTestAsync(Services, "sd-paging-tester", farmId, companyId, Original(clientId), clientId: clientId);
            ids.Add((row.Id, clientId));
        }

        var seen = new List<JsonNode>();
        var first = JsonNode.Parse(await tester.GetStringAsync("/api/sync/tests?limit=3"))!;
        seen.AddRange(first["tests"]!.AsArray()!);
        var cursor = first["next"]!.GetValue<string>();
        // Mid-pull: one test already delivered and one still to come are deleted.
        var delivered = ids.Single(x => x.ClientId == seen[0]["clientId"]!.GetValue<Guid>());
        var toCome = ids.First(x => seen.All(s => s["clientId"]!.GetValue<Guid>() != x.ClientId));
        (await DeleteAsync(admin, delivered.Id)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await DeleteAsync(admin, toCome.Id)).StatusCode.Should().Be(HttpStatusCode.OK);

        while (cursor is not null)
        {
            var page = JsonNode.Parse(await tester.GetStringAsync($"/api/sync/tests?limit=3&cursor={cursor}"))!;
            seen.AddRange(page["tests"]!.AsArray()!);
            cursor = page["next"]?.GetValue<string>();
        }

        seen.Select(s => s["clientId"]!.GetValue<Guid>()).Should().BeEquivalentTo(ids.Select(x => x.ClientId)).And.OnlyHaveUniqueItems();
        seen.Single(s => s["clientId"]!.GetValue<Guid>() == toCome.ClientId)["deleted"]!.GetValue<bool>().Should().BeTrue();
    }

    // Unsent edits can only be on a draft: a signed-off version never changes (SyncController.FrozenAsync).
    [Fact]
    public async Task Edits_a_device_sends_after_the_deletion_are_kept_with_the_deleted_test()
    {
        var a = await ArrangeAsync("sd-push");
        var tester = _factory.CreateClientAs(Roles.Tester, a.TesterId);
        // The tester had reopened version 2; their draft of version 3 is on the server.
        var v3 = Guid.NewGuid();
        var draft = Original(v3);
        draft["version"] = 3;
        draft["supersedesId"] = a.V2.ClientId.ToString();
        object Push(string notes)
        {
            draft["notes"] = notes;
            return new
            {
                clientId = v3, farmName = "Kowhai Flats", notes, payloadJson = draft.ToJsonString(),
                version = 3, supersedesClientId = a.V2.ClientId,
            };
        }
        (await tester.PostAsJsonAsync("/api/sync/tests", Push("Started"))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await DeleteAsync(a.Admin, a.V1.Id)).StatusCode.Should().Be(HttpStatusCode.OK);

        var res = await tester.PostAsJsonAsync("/api/sync/tests", Push("Edited offline before the deletion arrived"));

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await res.Content.ReadFromJsonAsync<JsonObject>())!;
        body["status"]!.GetValue<string>().Should().Be("deleted");
        body["reason"]!.GetValue<string>().Should().Be("Entered against the wrong farm");
        var row = await WithDbAsync(db => db.MachineTests.SingleAsync(t => t.ClientId == v3));
        row.IsDeleted.Should().BeTrue();
        row.Notes.Should().Be("Edited offline before the deletion arrived");

        // A signed-off version of the deleted test, re-sent as it was, is just a harmless retry.
        var resend = await tester.PostAsJsonAsync("/api/sync/tests", new
        {
            clientId = a.V2.ClientId, farmName = "Kowhai Flats", markedCompleteAt = a.V2.MarkedCompleteAt,
            payloadJson = a.V2.PayloadJson, version = 2, supersedesClientId = a.V1.ClientId,
        });
        resend.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resend.Content.ReadFromJsonAsync<JsonObject>())!["status"]!.GetValue<string>().Should().Be("unchanged");
    }

    [Fact]
    public async Task A_new_version_of_a_deleted_test_is_stored_deleted_too()
    {
        var a = await ArrangeAsync("sd-newver");
        var tester = _factory.CreateClientAs(Roles.Tester, a.TesterId);
        (await DeleteAsync(a.Admin, a.V1.Id)).StatusCode.Should().Be(HttpStatusCode.OK);
        var v3 = Guid.NewGuid();

        var res = await tester.PostAsJsonAsync("/api/sync/tests", new
        {
            clientId = v3, farmName = "Kowhai Flats", markedCompleteAt = DateTimeOffset.UtcNow,
            payloadJson = Original(v3).ToJsonString(), version = 3, supersedesClientId = a.V2.ClientId,
        });

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.Content.ReadFromJsonAsync<JsonObject>())!["status"]!.GetValue<string>().Should().Be("deleted");
        var row = await WithDbAsync(db => db.MachineTests.SingleAsync(t => t.ClientId == v3));
        row.IsDeleted.Should().BeTrue();
        row.DeletedReason.Should().Be("Entered against the wrong farm");
    }

    [Fact]
    public async Task Restoring_brings_every_version_back_and_tells_devices()
    {
        var a = await ArrangeAsync("sd-restore");
        var tester = _factory.CreateClientAs(Roles.Tester, a.TesterId);
        (await DeleteAsync(a.Admin, a.V1.Id)).StatusCode.Should().Be(HttpStatusCode.OK);
        var watermark = JsonNode.Parse(await tester.GetStringAsync("/api/sync/tests"))!["watermark"]!.GetValue<DateTimeOffset>();

        var res = await a.Admin.PostAsync($"/api/admin/tests/{a.V2.Id}/restore", null);

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await WithDbAsync(db => db.MachineTests.Where(t => t.TesterId == a.TesterId).ToListAsync());
        rows.Should().OnlyContain(t => !t.IsDeleted && t.DeletedAt == null && t.DeletedReason == null);
        (await WithDbAsync(db => db.AuditEntries.AnyAsync(e => e.Operation == "Restored" && e.Actor == "sd-restore-admin"))).Should().BeTrue();
        var since = Uri.EscapeDataString(watermark.ToString("o"));
        var pulled = JsonNode.Parse(await tester.GetStringAsync($"/api/sync/tests?since={since}"))!["tests"]!.AsArray();
        pulled.Where(t => t!["clientId"]!.GetValue<Guid>() == a.V2.ClientId).Should().ContainSingle()
            .Which!["deleted"]!.GetValue<bool>().Should().BeFalse();
        (await a.Admin.PostAsync($"/api/admin/tests/{a.V2.Id}/restore", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
