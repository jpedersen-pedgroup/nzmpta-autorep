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

// Sync reconciliation (PRD §Decisions). Every edit is a new version, so the collision is two versions
// replacing the same one: here, a tester's offline edit of a completed test reaching the server after
// an administrator has edited it online. The server answers the tester's signed-off version with a
// 409 carrying the base and the head, records the collision, and stores the device's combine of the
// two — keeping every version.
public class SyncReconciliationTests : IClassFixture<AuthedWebAppFactory>
{
    private readonly AuthedWebAppFactory _factory;
    public SyncReconciliationTests(AuthedWebAppFactory factory) => _factory = factory;

    private IServiceProvider Services => _factory.Services;

    private async Task<T> WithDbAsync<T>(Func<AutorepDbContext, Task<T>> f)
    {
        using var scope = Services.CreateScope();
        return await f(scope.ServiceProvider.GetRequiredService<AutorepDbContext>());
    }

    private sealed record Arranged(string TesterId, Guid CompanyId, Guid FarmId, MachineTest V1, JsonObject V1Payload,
        Guid AdminClientId, JsonObject AdminPayload, HttpClient Tester);

    /// <summary>Version 1 on the server, and an administrator's version 2 of it (comments changed).</summary>
    private async Task<Arranged> ArrangeAsync(string tag)
    {
        var (companyId, farmId) = await SeedCompanyAsync(Services, tag);
        var testerId = $"{tag}-tester";
        await SeedUserAsync(Services, testerId, companyId);
        await SeedUserAsync(Services, $"{tag}-admin", null, "Sam Superadmin");
        var v1Id = Guid.NewGuid();
        var v1Payload = Original(v1Id);
        var v1 = await SeedTestAsync(Services, testerId, farmId, companyId, v1Payload, clientId: v1Id);

        var admin = _factory.CreateClientAs(Roles.SuperAdministrator, $"{tag}-admin");
        var res = await admin.PostAsJsonAsync($"/api/admin/tests/{v1.Id}/versions", new
        {
            payloadJson = Edited(v1Payload, p => p["notes"] = "Admin's comment").ToJsonString(),
            reason = "Comment corrected",
        });
        res.StatusCode.Should().Be(HttpStatusCode.Created, await res.Content.ReadAsStringAsync());
        var adminClientId = (await res.Content.ReadFromJsonAsync<JsonObject>())!["clientId"]!.GetValue<Guid>();
        var adminPayload = JsonNode.Parse(await WithDbAsync(db =>
            db.MachineTests.Where(t => t.ClientId == adminClientId).Select(t => t.PayloadJson!).SingleAsync()))!.AsObject();

        return new Arranged(testerId, companyId, farmId, v1, v1Payload, adminClientId, adminPayload,
            _factory.CreateClientAs(Roles.Tester, testerId));
    }

    /// <summary>The tester's own version 2, made offline from version 1.</summary>
    private static object TesterVersion(Arranged a, Guid clientId, bool signedOff, Action<JsonObject>? change = null)
    {
        var payload = (JsonObject)a.V1Payload.DeepClone();
        payload["id"] = clientId.ToString();
        payload["version"] = 2;
        payload["supersedesId"] = a.V1.ClientId.ToString();
        payload["readings"]!["tr.workingVacuum"] = 46;
        change?.Invoke(payload);
        return new
        {
            clientId,
            farmName = "Kowhai Flats",
            notes = payload["notes"]?.GetValue<string>(),
            markedCompleteAt = signedOff ? DateTimeOffset.UtcNow : (DateTimeOffset?)null,
            createdAt = DateTimeOffset.UtcNow.AddHours(-2),
            payloadJson = payload.ToJsonString(),
            version = 2,
            supersedesClientId = a.V1.ClientId,
        };
    }

    /// <summary>The device's combine, following the rule: the admin's version with the tester's reading
    /// (and, where the tester changed the comments too, theirs — the later arrival).</summary>
    private static object Combined(Arranged a, Guid clientId, Guid incoming, Guid head, int version = 3,
        string notes = "Admin's comment", Action<JsonObject>? tamper = null) => new
    {
        clientId,
        farmName = "Kowhai Flats",
        notes,
        markedCompleteAt = DateTimeOffset.UtcNow,
        createdAt = DateTimeOffset.UtcNow,
        payloadJson = Edited(a.AdminPayload, p =>
        {
            p["id"] = clientId.ToString();
            p["version"] = version;
            p["supersedesId"] = head.ToString();
            p["mergedFromId"] = incoming.ToString();
            p["readings"]!["tr.workingVacuum"] = 46;
            p["notes"] = notes;
            tamper?.Invoke(p);
        }, version).ToJsonString(),
        version,
        supersedesClientId = head,
        mergedFromClientId = incoming,
    };

    // ---- Detection on push ---------------------------------------------------------------

    [Fact]
    public async Task A_signed_off_version_arriving_after_an_admin_edit_gets_a_409_with_what_the_device_needs()
    {
        var a = await ArrangeAsync("rc-409");
        var mine = Guid.NewGuid();

        var res = await a.Tester.PostAsJsonAsync("/api/sync/tests", TesterVersion(a, mine, signedOff: true));

        res.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = (await res.Content.ReadFromJsonAsync<JsonObject>())!;
        body["conflict"]!.GetValue<string>().Should().Be("superseded");
        body["baseClientId"]!.GetValue<Guid>().Should().Be(a.V1.ClientId!.Value);
        body["base"]!["clientId"]!.GetValue<Guid>().Should().Be(a.V1.ClientId!.Value);
        body["head"]!["clientId"]!.GetValue<Guid>().Should().Be(a.AdminClientId);
        body["headVersion"]!.GetValue<int>().Should().Be(2);
        JsonNode.Parse(body["head"]!["payloadJson"]!.GetValue<string>())!["notes"]!.GetValue<string>().Should().Be("Admin's comment");

        (await WithDbAsync(db => db.MachineTests.AnyAsync(t => t.ClientId == mine)))
            .Should().BeFalse("nothing is stored until the two have been combined");
        var conflict = await WithDbAsync(db => db.SyncConflicts.SingleAsync(c => c.IncomingClientId == mine));
        conflict.Status.Should().Be(SyncConflictStatus.Pending);
        conflict.DetectedOn.Should().Be(SyncConflictSource.Push);
        conflict.HeadClientId.Should().Be(a.AdminClientId);
    }

    [Fact]
    public async Task An_unfinished_version_is_stored_as_usual_and_the_collision_recorded_once()
    {
        var a = await ArrangeAsync("rc-draft");
        var mine = Guid.NewGuid();

        var first = await a.Tester.PostAsJsonAsync("/api/sync/tests", TesterVersion(a, mine, signedOff: false));
        var again = await a.Tester.PostAsJsonAsync("/api/sync/tests", TesterVersion(a, mine, signedOff: false, p => p["notes"] = "still going"));

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        var row = await WithDbAsync(db => db.MachineTests.SingleAsync(t => t.ClientId == mine));
        row.MarkedCompleteAt.Should().BeNull();
        row.RootClientId.Should().Be(a.V1.ClientId);
        (await WithDbAsync(db => db.SyncConflicts.CountAsync(c => c.IncomingClientId == mine))).Should().Be(1);
    }

    // ---- The merge ----------------------------------------------------------------------

    [Fact]
    public async Task The_merge_keeps_both_versions_and_stores_the_combined_one_as_current()
    {
        var a = await ArrangeAsync("rc-merge");
        var mine = Guid.NewGuid();
        var combined = Guid.NewGuid();
        (await a.Tester.PostAsJsonAsync("/api/sync/tests", TesterVersion(a, mine, signedOff: true)))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        var res = await a.Tester.PostAsJsonAsync("/api/sync/tests/merge", new
        {
            incoming = TesterVersion(a, mine, signedOff: true, p => p["notes"] = "Tester's comment"),
            // Both changed the comments: the tester's version arrived second, so its comment stands.
            merged = Combined(a, combined, mine, a.AdminClientId, notes: "Tester's comment"),
            headClientId = a.AdminClientId,
        });

        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        (await res.Content.ReadFromJsonAsync<JsonObject>())!["status"]!.GetValue<string>().Should().Be("merged");

        var incoming = await WithDbAsync(db => db.MachineTests.SingleAsync(t => t.ClientId == mine));
        incoming.MarkedCompleteAt.Should().NotBeNull("the tester's own version is kept, signed off");
        incoming.SupersedesClientId.Should().Be(a.V1.ClientId);
        incoming.TestingCompanyId.Should().Be(a.CompanyId);
        incoming.FarmId.Should().Be(a.FarmId);

        var merged = await WithDbAsync(db => db.MachineTests.Include(t => t.Configuration).SingleAsync(t => t.ClientId == combined));
        merged.TesterId.Should().Be(a.TesterId);
        merged.Version.Should().Be(3);
        merged.SupersedesClientId.Should().Be(a.AdminClientId);
        merged.MergedFromClientId.Should().Be(mine);
        merged.RootClientId.Should().Be(a.V1.ClientId);
        merged.TestingCompanyId.Should().Be(a.CompanyId);
        merged.Configuration.Should().NotBeNull();
        merged.Notes.Should().Be("Tester's comment", "mirrored from the checked payload");

        var conflict = await WithDbAsync(db => db.SyncConflicts.SingleAsync(c => c.IncomingClientId == mine));
        conflict.Status.Should().Be(SyncConflictStatus.Merged);
        conflict.MergedClientId.Should().Be(combined);
        conflict.ResolvedAt.Should().NotBeNull();
        // Both changed the comments (to different words); only the tester changed the reading.
        JsonSerializer.Deserialize<string[]>(conflict.OverlappingFieldsJson!).Should().BeEquivalentTo(["notes"]);

        // Every list now shows the combined version alone.
        var list = JsonNode.Parse(await a.Tester.GetStringAsync("/api/tests"))!;
        list["items"]!.AsArray().Select(i => i!["id"]!.GetValue<Guid>()).Should().BeEquivalentTo([merged.Id]);
    }

    [Fact]
    public async Task A_merge_against_a_head_that_has_moved_on_is_answered_with_the_new_head()
    {
        var a = await ArrangeAsync("rc-moved");
        var mine = Guid.NewGuid();
        // The admin edits again before the device's combine arrives: version 3 is now current.
        var admin = _factory.CreateClientAs(Roles.SuperAdministrator, "rc-moved-admin");
        var adminRowId = await WithDbAsync(db => db.MachineTests.Where(t => t.ClientId == a.AdminClientId).Select(t => t.Id).SingleAsync());
        (await admin.PostAsJsonAsync($"/api/admin/tests/{adminRowId}/versions", new
        {
            payloadJson = Edited(a.AdminPayload, p => p["notes"] = "Admin again", version: 3).ToJsonString(),
            reason = "Again",
        })).StatusCode.Should().Be(HttpStatusCode.Created);

        var res = await a.Tester.PostAsJsonAsync("/api/sync/tests/merge", new
        {
            incoming = TesterVersion(a, mine, signedOff: true),
            merged = Combined(a, Guid.NewGuid(), mine, a.AdminClientId),
            headClientId = a.AdminClientId,
        });

        res.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = (await res.Content.ReadFromJsonAsync<JsonObject>())!;
        body["headVersion"]!.GetValue<int>().Should().Be(3);
        (await WithDbAsync(db => db.MachineTests.AnyAsync(t => t.ClientId == mine))).Should().BeFalse();
    }

    [Fact]
    public async Task Sending_the_same_merge_twice_stores_it_once()
    {
        var a = await ArrangeAsync("rc-twice");
        var mine = Guid.NewGuid();
        var combined = Guid.NewGuid();
        var request = new
        {
            incoming = TesterVersion(a, mine, signedOff: true),
            merged = Combined(a, combined, mine, a.AdminClientId),
            headClientId = a.AdminClientId,
        };

        (await a.Tester.PostAsJsonAsync("/api/sync/tests/merge", request)).StatusCode.Should().Be(HttpStatusCode.OK);
        var second = await a.Tester.PostAsJsonAsync("/api/sync/tests/merge", request);

        second.StatusCode.Should().Be(HttpStatusCode.OK);
        (await second.Content.ReadFromJsonAsync<JsonObject>())!["status"]!.GetValue<string>().Should().Be("already-merged");
        (await WithDbAsync(db => db.MachineTests.CountAsync(t => t.ClientId == combined))).Should().Be(1);
        // And a re-push of the merged-in version after the fact is just an update, not a new collision.
        var push = await a.Tester.PostAsJsonAsync("/api/sync/tests", TesterVersion(a, mine, signedOff: true));
        push.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_merge_must_name_the_incoming_version_and_follow_the_head()
    {
        var a = await ArrangeAsync("rc-shape");
        var mine = Guid.NewGuid();

        var wrongParent = await a.Tester.PostAsJsonAsync("/api/sync/tests/merge", new
        {
            incoming = TesterVersion(a, mine, signedOff: true),
            merged = Combined(a, Guid.NewGuid(), Guid.NewGuid(), a.AdminClientId),
            headClientId = a.AdminClientId,
        });
        var tooLow = await a.Tester.PostAsJsonAsync("/api/sync/tests/merge", new
        {
            incoming = TesterVersion(a, mine, signedOff: true),
            merged = Combined(a, Guid.NewGuid(), mine, a.AdminClientId, version: 2),
            headClientId = a.AdminClientId,
        });

        wrongParent.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        tooLow.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // Codex review of #80: the server holds the combine to the rule itself, from the stored versions.
    [Theory]
    [InlineData("drops-admin-change", "notes")]
    [InlineData("invents-a-change", "recommendations.vp.wick")]
    [InlineData("drops-tester-change", "readings.tr.workingVacuum")]
    [InlineData("drops-attestations", "attestations")]
    [InlineData("rewrites-history", "amendments")]
    public async Task A_combine_that_departs_from_the_rule_is_refused(string tampering, string field)
    {
        var a = await ArrangeAsync($"rc-tamper-{tampering}");
        var mine = Guid.NewGuid();
        Action<JsonObject> tamper = tampering switch
        {
            "drops-admin-change" => p => p["notes"] = "Original comment",
            "invents-a-change" => p => p["recommendations"]!["vp.wick"] = "Something nobody wrote",
            "drops-tester-change" => p => p["readings"]!["tr.workingVacuum"] = 48,
            "drops-attestations" => p => p["attestations"] = new JsonArray(),
            _ => p => p["amendments"]![0]!["reason"] = "A kinder reason",
        };

        var res = await a.Tester.PostAsJsonAsync("/api/sync/tests/merge", new
        {
            incoming = TesterVersion(a, mine, signedOff: true),
            merged = Combined(a, Guid.NewGuid(), mine, a.AdminClientId, tamper: tamper),
            headClientId = a.AdminClientId,
        });

        res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = (await res.Content.ReadFromJsonAsync<JsonObject>())!;
        body["fields"]!.AsArray().Select(f => f!.GetValue<string>()).Should().Contain(field);
        (await WithDbAsync(db => db.MachineTests.AnyAsync(t => t.ClientId == mine || t.MergedFromClientId == mine)))
            .Should().BeFalse("nothing is stored from a combine that doesn't follow the rule");
    }

    [Fact]
    public async Task The_combined_versions_own_links_are_the_servers_whatever_its_payload_says()
    {
        var a = await ArrangeAsync("rc-links");
        var mine = Guid.NewGuid();
        var combined = Guid.NewGuid();

        var res = await a.Tester.PostAsJsonAsync("/api/sync/tests/merge", new
        {
            incoming = TesterVersion(a, mine, signedOff: true),
            merged = Combined(a, combined, mine, a.AdminClientId, tamper: p => p["supersedesId"] = Guid.NewGuid().ToString()),
            headClientId = a.AdminClientId,
        });

        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var payload = JsonNode.Parse(await WithDbAsync(db => db.MachineTests.Where(t => t.ClientId == combined).Select(t => t.PayloadJson!).SingleAsync()))!;
        payload["supersedesId"]!.GetValue<string>().Should().Be(a.AdminClientId.ToString());
        payload["mergedFromId"]!.GetValue<string>().Should().Be(mine.ToString());
    }

    // Codex review of #80: an administrator's version carries the tester's id, so the tester's device
    // knows its ClientId. A signed-off version must never change in place through the sync push.
    [Fact]
    public async Task A_signed_off_version_cant_be_overwritten_through_the_push()
    {
        var a = await ArrangeAsync("rc-frozen");
        var before = await WithDbAsync(db => db.MachineTests.AsNoTracking().SingleAsync(t => t.ClientId == a.AdminClientId));
        var tampered = (JsonObject)a.AdminPayload.DeepClone();
        tampered["notes"] = "Quietly changed back";

        var change = await a.Tester.PostAsJsonAsync("/api/sync/tests", new
        {
            clientId = a.AdminClientId, farmName = "Kowhai Flats", markedCompleteAt = before.MarkedCompleteAt,
            notes = "Quietly changed back", payloadJson = tampered.ToJsonString(), version = 2, supersedesClientId = a.V1.ClientId,
        });
        var uncomplete = await a.Tester.PostAsJsonAsync("/api/sync/tests", new
        {
            clientId = a.AdminClientId, farmName = "Kowhai Flats", markedCompleteAt = (DateTimeOffset?)null,
            payloadJson = a.AdminPayload.ToJsonString(), version = 2, supersedesClientId = a.V1.ClientId,
        });
        // The tester's device marks the version it reopens read-only and re-sends it: bookkeeping
        // only, so it's a harmless retry.
        var retry = (JsonObject)a.AdminPayload.DeepClone();
        retry["readonly"] = true;
        retry["syncState"] = "local-only";
        var resend = await a.Tester.PostAsJsonAsync("/api/sync/tests", new
        {
            clientId = a.AdminClientId, farmName = "Kowhai Flats", markedCompleteAt = before.MarkedCompleteAt,
            payloadJson = retry.ToJsonString(), version = 2, supersedesClientId = a.V1.ClientId,
        });

        change.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = (await change.Content.ReadFromJsonAsync<JsonObject>())!;
        body["error"]!.GetValue<string>().Should().Be("completed");
        body["fields"]!.AsArray().Select(f => f!.GetValue<string>()).Should().Equal("notes");
        uncomplete.StatusCode.Should().Be(HttpStatusCode.Conflict);
        resend.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resend.Content.ReadFromJsonAsync<JsonObject>())!["status"]!.GetValue<string>().Should().Be("unchanged");

        var after = await WithDbAsync(db => db.MachineTests.AsNoTracking().SingleAsync(t => t.ClientId == a.AdminClientId));
        after.PayloadJson.Should().Be(before.PayloadJson);
        after.Notes.Should().Be(before.Notes);
        after.MarkedCompleteAt.Should().Be(before.MarkedCompleteAt);
        after.UpdatedAt.Should().Be(before.UpdatedAt, "a retry that changes nothing writes nothing");
    }

    [Fact]
    public async Task Another_tester_cannot_merge_into_someone_elses_test()
    {
        var a = await ArrangeAsync("rc-idor");
        var intruder = _factory.CreateClientAs(Roles.Tester, "rc-idor-intruder");
        var theirs = Guid.NewGuid();

        // A well-formed merge, naming another tester's test as its base.
        var res = await intruder.PostAsJsonAsync("/api/sync/tests/merge", new
        {
            incoming = TesterVersion(a, theirs, signedOff: true),
            merged = Combined(a, Guid.NewGuid(), theirs, a.AdminClientId),
            headClientId = a.AdminClientId,
        });

        res.StatusCode.Should().Be(HttpStatusCode.NotFound, "ClientIds only resolve within the caller's own tests");
        (await WithDbAsync(db => db.MachineTests.CountAsync(t => t.TesterId == "rc-idor-intruder"))).Should().Be(0);
    }

    // ---- Chains, company and farm ---------------------------------------------------------

    [Fact]
    public async Task A_new_version_stays_with_its_tests_farm_and_company_after_the_tester_moves()
    {
        var (companyA, farmA) = await SeedCompanyAsync(Services, "rc-move-a");
        var (companyB, _) = await SeedCompanyAsync(Services, "rc-move-b");
        await SeedUserAsync(Services, "rc-move-tester", companyB); // moved to B since version 1
        var v1Id = Guid.NewGuid();
        var v1 = await SeedTestAsync(Services, "rc-move-tester", farmA, companyA, Original(v1Id), clientId: v1Id);
        var tester = _factory.CreateClientAs(Roles.Tester, "rc-move-tester");
        var v2 = Guid.NewGuid();

        var res = await tester.PostAsJsonAsync("/api/sync/tests", new
        {
            clientId = v2, farmName = "Kowhai Flats", markedCompleteAt = DateTimeOffset.UtcNow,
            payloadJson = Original(v2).ToJsonString(), version = 2, supersedesClientId = v1Id,
        });

        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var row = await WithDbAsync(db => db.MachineTests.SingleAsync(t => t.ClientId == v2));
        row.TestingCompanyId.Should().Be(companyA, "an amendment is more work on the original test");
        row.FarmId.Should().Be(farmA);
        row.RootClientId.Should().Be(v1.ClientId);
    }

    [Fact]
    public async Task A_version_pushed_before_its_parent_is_adopted_when_the_parent_arrives()
    {
        var (companyId, _) = await SeedCompanyAsync(Services, "rc-order");
        await SeedUserAsync(Services, "rc-order-tester", companyId);
        var tester = _factory.CreateClientAs(Roles.Tester, "rc-order-tester");
        Guid v1 = Guid.NewGuid(), v2 = Guid.NewGuid(), v3 = Guid.NewGuid();

        async Task PushAsync(Guid id, int version, Guid? parent) =>
            (await tester.PostAsJsonAsync("/api/sync/tests", new
            {
                clientId = id, farmName = "Order Farm", markedCompleteAt = DateTimeOffset.UtcNow,
                payloadJson = Original(id).ToJsonString(), version, supersedesClientId = parent,
            })).IsSuccessStatusCode.Should().BeTrue();

        // A device pushes in whatever order its store lists them.
        await PushAsync(v3, 3, v2);
        await PushAsync(v2, 2, v1);
        await PushAsync(v1, 1, null);

        var roots = await WithDbAsync(db => db.MachineTests.Where(t => t.TesterId == "rc-order-tester")
            .Select(t => t.RootClientId).ToListAsync());
        roots.Should().AllBeEquivalentTo(v1);
    }

    // ---- Two writers, one parent -----------------------------------------------------------

    [Fact]
    public async Task The_successor_stamp_stops_two_writers_both_replacing_the_same_version()
    {
        var (companyId, farmId) = await SeedCompanyAsync(Services, "rc-race");
        await SeedUserAsync(Services, "rc-race-tester", companyId);
        var v1 = await SeedTestAsync(Services, "rc-race-tester", farmId, companyId, Original(Guid.NewGuid()));

        using var scopeA = Services.CreateScope();
        using var scopeB = Services.CreateScope();
        var dbA = scopeA.ServiceProvider.GetRequiredService<AutorepDbContext>();
        var dbB = scopeB.ServiceProvider.GetRequiredService<AutorepDbContext>();
        var seenByA = await dbA.MachineTests.SingleAsync(t => t.Id == v1.Id);
        var seenByB = await dbB.MachineTests.SingleAsync(t => t.Id == v1.Id);

        seenByA.SuccessorStamp = Guid.NewGuid();
        await dbA.SaveChangesAsync();
        seenByB.SuccessorStamp = Guid.NewGuid();

        await dbB.Invoking(db => db.SaveChangesAsync()).Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task Renewing_a_stamp_is_not_audited_as_a_change_to_the_version()
    {
        var (companyId, farmId) = await SeedCompanyAsync(Services, "rc-stamp-audit");
        var v1 = await SeedTestAsync(Services, "rc-stamp-audit-tester", farmId, companyId, Original(Guid.NewGuid()));

        await WithDbAsync(async db =>
        {
            (await db.MachineTests.SingleAsync(t => t.Id == v1.Id)).SuccessorStamp = Guid.NewGuid();
            return await db.SaveChangesAsync();
        });

        (await WithDbAsync(db => db.AuditEntries.CountAsync(e => e.EntityKey == v1.Id.ToString() && e.Operation == "Modified")))
            .Should().Be(0);
    }
}
