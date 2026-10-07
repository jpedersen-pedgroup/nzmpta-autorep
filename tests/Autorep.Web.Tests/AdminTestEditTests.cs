using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Autorep.Web.Services;
using Autorep.Web.Services.Pdfs;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Autorep.Web.Tests.TestPayloads;

namespace Autorep.Web.Tests;

// O2 + PRD stories 49–50: an administrator edits a completed test, and the edit is saved as the
// test's next version — never in place. A Super-Administrator may change any field the wizard edits;
// a Company Administrator only the fault summary's recommendations and general comments, on their own
// company's tests. The server holds both to that by comparing the edit with the stored version it was
// made from.
public class AdminTestEditTests : IClassFixture<AuthedWebAppFactory>
{
    private readonly AuthedWebAppFactory _factory;
    public AdminTestEditTests(AuthedWebAppFactory factory) => _factory = factory;

    private IServiceProvider Services => _factory.Services;

    private async Task<T> WithDbAsync<T>(Func<AutorepDbContext, Task<T>> f)
    {
        using var scope = Services.CreateScope();
        return await f(scope.ServiceProvider.GetRequiredService<AutorepDbContext>());
    }

    /// <summary>A company, its tester, a company administrator, and a completed test of the tester's.</summary>
    private async Task<(Guid CompanyId, Guid FarmId, MachineTest Test, JsonObject Payload)> ArrangeAsync(string tag)
    {
        var (companyId, farmId) = await SeedCompanyAsync(Services, tag);
        await SeedUserAsync(Services, $"{tag}-tester", companyId, "Ellie Tester");
        await SeedUserAsync(Services, $"{tag}-coadmin", companyId, "Cora Companyadmin");
        var clientId = Guid.NewGuid();
        var payload = Original(clientId);
        var test = await SeedTestAsync(Services, $"{tag}-tester", farmId, companyId, payload, clientId: clientId);
        return (companyId, farmId, test, payload);
    }

    private async Task<HttpClient> SuperAdminAsync(string id)
    {
        if (!await WithDbAsync(db => db.Users.AnyAsync(u => u.Id == id)))
            await SeedUserAsync(Services, id, null, "Sam Superadmin");
        return _factory.CreateClientAs(Roles.SuperAdministrator, id);
    }

    private static Task<HttpResponseMessage> SaveAsync(HttpClient client, Guid baseId, JsonObject edited, string? reason = "Corrected on the farmer's call") =>
        client.PostAsJsonAsync($"/api/admin/tests/{baseId}/versions", new { payloadJson = edited.ToJsonString(), reason });

    private sealed record Saved(Guid Id, Guid ClientId, int Version);

    // ---- Super-Administrator: any field, as a new version ----------------------------------

    [Fact]
    public async Task A_super_admin_edit_is_saved_as_the_next_version_owned_by_the_original_tester()
    {
        var (companyId, farmId, test, payload) = await ArrangeAsync("sa-new");
        var admin = await SuperAdminAsync("sa-new-admin");

        var res = await SaveAsync(admin, test.Id, Edited(payload, p => p["readings"]!["tr.workingVacuum"] = 47));

        res.StatusCode.Should().Be(HttpStatusCode.Created, await res.Content.ReadAsStringAsync());
        var saved = (await res.Content.ReadFromJsonAsync<Saved>())!;
        saved.Version.Should().Be(2);

        var row = await WithDbAsync(db => db.MachineTests.SingleAsync(t => t.Id == saved.Id));
        row.TesterId.Should().Be("sa-new-tester", "the version must reach the tester's device and history");
        row.AuthorId.Should().Be("sa-new-admin");
        row.TestingCompanyId.Should().Be(companyId);
        row.FarmId.Should().Be(farmId);
        row.ClientId.Should().Be(saved.ClientId).And.NotBe(test.ClientId!.Value);
        row.SupersedesClientId.Should().Be(test.ClientId);
        row.RootClientId.Should().Be(test.ClientId);
        row.MarkedCompleteAt.Should().Be(test.MarkedCompleteAt,
            "the test was completed when the tester signed it off; the edit's own time is in its record");

        var stored = JsonNode.Parse(row.PayloadJson!)!;
        stored["id"]!.GetValue<string>().Should().Be(saved.ClientId.ToString());
        stored["version"]!.GetValue<int>().Should().Be(2);
        stored["supersedesId"]!.GetValue<string>().Should().Be(test.ClientId.ToString());
        stored["readings"]!["tr.workingVacuum"]!.GetValue<int>().Should().Be(47);
        var record = stored["amendments"]!.AsArray().Single()!;
        record["version"]!.GetValue<int>().Should().Be(2);
        record["baseVersion"]!.GetValue<int>().Should().Be(1);
        record["amendedBy"]!.GetValue<string>().Should().Be("sa-new-admin@local");
        record["amendedByName"]!.GetValue<string>().Should().Be("Sam Superadmin");
        record["amendedByRole"]!.GetValue<string>().Should().Be("Super Administrator");
        record["reason"]!.GetValue<string>().Should().Be("Corrected on the farmer's call");
        record["amendedAt"]!.GetValue<string>().Should().NotBe(stored["markedCompleteAt"]!.GetValue<string>());
        // The tester's attestations stand: the edit didn't redo the inspection.
        stored["attestations"]!.AsArray().Should().HaveCount(2);

        // The version it replaced is untouched — nothing is ever edited in place.
        var original = await WithDbAsync(db => db.MachineTests.SingleAsync(t => t.Id == test.Id));
        original.PayloadJson.Should().Be(payload.ToJsonString());
    }

    [Fact]
    public async Task The_new_version_mirrors_its_configuration_comments_and_next_test_date_into_columns()
    {
        var (_, _, test, payload) = await ArrangeAsync("sa-cols");
        var admin = await SuperAdminAsync("sa-cols-admin");

        var res = await SaveAsync(admin, test.Id, Edited(payload, p =>
        {
            p["config"]!["clusterCount"] = 24;
            p["notes"] = "Comment fixed";
            p["nextTestDate"] = "2027-06-30";
        }));
        res.StatusCode.Should().Be(HttpStatusCode.Created, await res.Content.ReadAsStringAsync());
        var saved = (await res.Content.ReadFromJsonAsync<Saved>())!;

        var row = await WithDbAsync(db => db.MachineTests.Include(t => t.Configuration).SingleAsync(t => t.Id == saved.Id));
        row.Configuration!.ClusterCount.Should().Be(24);
        row.Configuration.PlantType.Should().Be(PlantType.Rotary);
        row.Notes.Should().Be("Comment fixed");
        row.NextTestDate.Should().Be(new DateOnly(2027, 6, 30));
    }

    [Fact]
    public async Task The_edit_is_audited_with_its_author_reason_and_changed_fields_but_no_values()
    {
        var (_, _, test, payload) = await ArrangeAsync("sa-audit");
        var admin = await SuperAdminAsync("sa-audit-admin");

        var res = await SaveAsync(admin, test.Id, Edited(payload, p => p["recommendations"]!["vp.wick"] = "Replace the wicks, Aroha"));
        var saved = (await res.Content.ReadFromJsonAsync<Saved>())!;

        var entries = await WithDbAsync(db => db.AuditEntries.Where(e => e.EntityKey == saved.Id.ToString()).ToListAsync());
        entries.Should().Contain(e => e.Operation == "Added" && e.Actor == "sa-audit-admin");
        var edit = entries.Single(e => e.Operation == "AdminVersionCreated");
        edit.Actor.Should().Be("sa-audit-admin");
        edit.AfterJson.Should().Contain("Corrected on the farmer's call")
            .And.Contain("recommendations.vp.wick")
            .And.NotContain("Aroha", "the audit store doesn't keep payload values");
    }

    [Fact]
    public async Task Who_did_the_test_and_at_which_farm_cant_be_changed_even_by_a_super_admin()
    {
        var (_, _, test, payload) = await ArrangeAsync("sa-fixed");
        var admin = await SuperAdminAsync("sa-fixed-admin");

        var res = await SaveAsync(admin, test.Id, Edited(payload, p =>
        {
            p["testedBy"] = new JsonObject { ["name"] = "Someone Else" };
            p["farmName"] = "Another Farm";
        }));

        res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await res.Content.ReadFromJsonAsync<JsonObject>();
        body!["fields"]!.AsArray().Select(f => f!.GetValue<string>()).Should().BeEquivalentTo(["farmName", "testedBy"]);
        (await WithDbAsync(db => db.MachineTests.CountAsync(t => t.SupersedesClientId == test.ClientId))).Should().Be(0);
    }

    // ---- The analyser PDF (O3, as part of an admin version) --------------------------------

    private static readonly string PdfA = Convert.ToBase64String("%PDF-1.4 analyser export A"u8.ToArray());
    private static readonly string PdfB = Convert.ToBase64String("%PDF-1.4 corrected analyser export B"u8.ToArray());

    private static JsonObject Attachment(string name, string? base64, string attachedAt, bool pointer = false)
    {
        var pdf = new JsonObject { ["name"] = name, ["size"] = 26, ["attachedAt"] = attachedAt };
        if (base64 is not null) pdf["base64"] = base64;
        if (pointer) pdf["onServer"] = true;
        return pdf;
    }

    private async Task<(MachineTest Test, JsonObject Payload)> ArrangeWithPdfAsync(string tag)
    {
        var (companyId, farmId) = await SeedCompanyAsync(Services, tag);
        await SeedUserAsync(Services, $"{tag}-tester", companyId);
        await SeedUserAsync(Services, $"{tag}-coadmin", companyId);
        var clientId = Guid.NewGuid();
        var payload = Original(clientId);
        payload["pulsationPdf"] = Attachment("pulse.pdf", PdfA, "2026-09-01T00:00:00.000Z");
        return (await SeedTestAsync(Services, $"{tag}-tester", farmId, companyId, payload, clientId: clientId), payload);
    }

    private InMemoryPdfStore Store => Services.GetRequiredService<InMemoryPdfStore>();

    private async Task<(Guid Id, string Payload)> SavedAsync(HttpResponseMessage res)
    {
        res.StatusCode.Should().Be(HttpStatusCode.Created, await res.Content.ReadAsStringAsync());
        var saved = (await res.Content.ReadFromJsonAsync<Saved>())!;
        return (saved.Id, await WithDbAsync(db => db.MachineTests.Where(t => t.Id == saved.Id).Select(t => t.PayloadJson!).SingleAsync()));
    }

    /// <summary>The analyser PDF as the admin viewer's report fetches it.</summary>
    private static async Task<byte[]> PdfOfAsync(HttpClient client, Guid testId)
    {
        var res = await client.GetAsync($"/api/tests/{testId}/pulsation-pdf");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        return await res.Content.ReadAsByteArrayAsync();
    }

    // Since the PDF store, a payload never keeps the bytes: an attachment is a pointer naming the
    // stored copy (sha256), and the copy stays under the version that first sent it (serverTestId).

    [Fact]
    public async Task A_super_admin_can_replace_the_analyser_pdf_as_part_of_a_new_version()
    {
        var (test, payload) = await ArrangeWithPdfAsync("pdf-replace");
        var admin = await SuperAdminAsync("pdf-replace-admin");

        var (id, stored) = await SavedAsync(await SaveAsync(admin, test.Id,
            Edited(payload, p => p["pulsationPdf"] = Attachment("pulse-corrected.pdf", PdfB, "2026-10-07T00:00:00.000Z"))));

        PulsationPayload.Base64(stored).Should().BeNull("the new PDF went to the PDF store");
        PulsationPayload.StoredSha256(stored).Should().Be(PdfHash.Sha256Hex(Convert.FromBase64String(PdfB)));
        PulsationPayload.HolderClientId(stored).Should().BeNull("it's stored under the new version itself");
        PulsationPayload.FileName(stored).Should().Be("pulse-corrected.pdf");
        (await PdfOfAsync(admin, id)).Should().Equal(Convert.FromBase64String(PdfB));
        (await PdfOfAsync(admin, test.Id)).Should().Equal(Convert.FromBase64String(PdfA), "the version it replaced keeps its own");
        var audit = await WithDbAsync(db => db.AuditEntries.SingleAsync(e => e.Operation == "AdminVersionCreated" && e.Actor == "pdf-replace-admin"));
        audit.AfterJson.Should().Contain("pulsationPdf").And.NotContain(PdfB, "no bytes in the audit store");
    }

    [Fact]
    public async Task A_carried_over_attachment_points_at_the_edited_versions_stored_copy()
    {
        // The usual case: the edited version's PDF is in the store, and the browser — which fetched the
        // view with attachments=omit — sends the pointer back unchanged.
        var (test, payload) = await ArrangeWithPdfAsync("pdf-stored");
        var bytes = Convert.FromBase64String(PdfA);
        var sha = PdfHash.Sha256Hex(bytes);
        await Store.PutAsync(PdfContainer.PulsationData, PdfKeys.Pulsation(test.TesterId, test.ClientId!.Value, sha), bytes, "application/pdf");
        await WithDbAsync(async db =>
        {
            var row = await db.MachineTests.SingleAsync(t => t.Id == test.Id);
            row.PayloadJson = PulsationPayload.AsStored(row.PayloadJson, sha, holderClientId: null);
            return await db.SaveChangesAsync();
        });
        var pointer = JsonNode.Parse(PulsationPayload.AsStored(payload.ToJsonString(), sha, null)!)!.AsObject();
        var admin = await SuperAdminAsync("pdf-stored-admin");
        var before = Store.Keys(PdfContainer.PulsationData).Count;

        var (id, stored) = await SavedAsync(await SaveAsync(admin, test.Id, Edited(pointer, p => p["notes"] = "Only the comment changed")));

        PulsationPayload.StoredSha256(stored).Should().Be(sha);
        PulsationPayload.HolderClientId(stored).Should().Be(test.ClientId!.Value, "the bytes stay under the version that sent them");
        Store.Keys(PdfContainer.PulsationData).Count.Should().Be(before, "nothing is copied");
        (await PdfOfAsync(admin, id)).Should().Equal(bytes);
    }

    [Fact]
    public async Task An_attachment_still_inline_in_the_edited_version_moves_to_the_store_with_the_new_one()
    {
        // The edited version was written before the PDF store, so its row still holds the bytes; the
        // browser was sent a pointer. The new version's copy goes to the store.
        var (test, payload) = await ArrangeWithPdfAsync("pdf-pointer");
        var admin = await SuperAdminAsync("pdf-pointer-admin");

        var (id, stored) = await SavedAsync(await SaveAsync(admin, test.Id, Edited(payload, p =>
        {
            p["pulsationPdf"] = Attachment("pulse.pdf", null, "2026-09-01T00:00:00.000Z", pointer: true);
            p["notes"] = "Only the comment changed";
        })));

        PulsationPayload.Base64(stored).Should().BeNull();
        PulsationPayload.StoredSha256(stored).Should().Be(PdfHash.Sha256Hex(Convert.FromBase64String(PdfA)));
        (await PdfOfAsync(admin, id)).Should().Equal(Convert.FromBase64String(PdfA));
    }

    [Fact]
    public async Task A_company_admin_cannot_touch_the_attachment()
    {
        var (test, payload) = await ArrangeWithPdfAsync("pdf-coadmin");
        var admin = _factory.CreateClientAs(Roles.CompanyAdministrator, "pdf-coadmin-coadmin");

        var res = await SaveAsync(admin, test.Id, Edited(payload, p => p.Remove("pulsationPdf")));

        res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await res.Content.ReadFromJsonAsync<JsonObject>())!["fields"]!.AsArray().Single()!.GetValue<string>().Should().Be("pulsationPdf");
    }

    // ---- Company Administrator: summary and recommendations, own company only --------------

    [Fact]
    public async Task A_company_admin_can_change_recommendations_and_comments_on_their_companys_test()
    {
        var (_, _, test, payload) = await ArrangeAsync("ca-ok");
        var admin = _factory.CreateClientAs(Roles.CompanyAdministrator, "ca-ok-coadmin");

        var res = await SaveAsync(admin, test.Id, Edited(payload, p =>
        {
            p["recommendations"]!["vp.wick"] = "Clean and re-oil the wicks";
            p["recommendations"]!["tr.workingVacuum"] = "Check the regulator";
            p["notes"] = "Polished for the client";
        }), "Clearer wording for the client");

        res.StatusCode.Should().Be(HttpStatusCode.Created, await res.Content.ReadAsStringAsync());
        var saved = (await res.Content.ReadFromJsonAsync<Saved>())!;
        var row = await WithDbAsync(db => db.MachineTests.SingleAsync(t => t.Id == saved.Id));
        row.AuthorId.Should().Be("ca-ok-coadmin");
        row.TesterId.Should().Be("ca-ok-tester");
        var record = JsonNode.Parse(row.PayloadJson!)!["amendments"]!.AsArray().Single()!;
        record["amendedByRole"]!.GetValue<string>().Should().Be("Company Administrator");
    }

    [Fact]
    public async Task A_company_admins_reading_change_is_refused_by_the_server_naming_the_field()
    {
        var (_, _, test, payload) = await ArrangeAsync("ca-reading");
        var admin = _factory.CreateClientAs(Roles.CompanyAdministrator, "ca-reading-coadmin");

        var res = await SaveAsync(admin, test.Id, Edited(payload, p =>
        {
            p["recommendations"]!["vp.wick"] = "Fine wording change";
            p["readings"]!["tr.workingVacuum"] = 46;
            p["config"]!["clusterCount"] = 30;
        }));

        res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await res.Content.ReadFromJsonAsync<JsonObject>();
        body!["fields"]!.AsArray().Select(f => f!.GetValue<string>())
            .Should().BeEquivalentTo(["config.clusterCount", "readings.tr.workingVacuum"]);
        (await WithDbAsync(db => db.MachineTests.CountAsync(t => t.SupersedesClientId == test.ClientId)))
            .Should().Be(0, "nothing is saved when any field is out of scope");
    }

    [Fact]
    public async Task A_company_admin_cannot_reach_another_companys_test()
    {
        var (_, _, test, payload) = await ArrangeAsync("ca-other");
        var (otherCompany, _) = await SeedCompanyAsync(Services, "ca-other-elsewhere");
        await SeedUserAsync(Services, "ca-other-outsider", otherCompany);
        var outsider = _factory.CreateClientAs(Roles.CompanyAdministrator, "ca-other-outsider");

        var res = await SaveAsync(outsider, test.Id, Edited(payload, p => p["notes"] = "Not my test"));

        res.StatusCode.Should().Be(HttpStatusCode.NotFound, "another company's test isn't disclosed, let alone edited");
    }

    [Fact]
    public async Task A_tester_cannot_use_the_admin_write_path()
    {
        var (_, _, test, payload) = await ArrangeAsync("tester-refused");
        var tester = _factory.CreateClientAs(Roles.Tester, "tester-refused-tester");

        var res = await SaveAsync(tester, test.Id, Edited(payload, p => p["notes"] = "Sneaky"));

        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---- What can be edited, and from where ---------------------------------------------

    [Fact]
    public async Task An_edit_made_from_a_version_that_has_since_been_replaced_is_a_409_naming_the_latest()
    {
        var (_, farmId, test, payload) = await ArrangeAsync("stale");
        var admin = await SuperAdminAsync("stale-admin");
        var first = await SaveAsync(admin, test.Id, Edited(payload, p => p["notes"] = "First edit"));
        var v2 = (await first.Content.ReadFromJsonAsync<Saved>())!;

        // A second admin still looking at version 1.
        var res = await SaveAsync(admin, test.Id, Edited(payload, p => p["notes"] = "Second edit"));

        res.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await res.Content.ReadFromJsonAsync<JsonObject>();
        body!["error"]!.GetValue<string>().Should().Be("superseded");
        body["latest"]!["id"]!.GetValue<Guid>().Should().Be(v2.Id);
        body["latest"]!["version"]!.GetValue<int>().Should().Be(2);
    }

    [Fact]
    public async Task In_progress_and_migrated_tests_cant_be_edited()
    {
        var (companyId, farmId, _, _) = await ArrangeAsync("blocked");
        var admin = await SuperAdminAsync("blocked-admin");
        var draftId = Guid.NewGuid();
        var draft = await SeedTestAsync(Services, "blocked-tester", farmId, companyId, Original(draftId), complete: false, clientId: draftId);
        var migrated = await SeedTestAsync(Services, "blocked-tester", farmId, companyId,
            new JsonObject { ["legacy"] = new JsonObject { ["TestNo"] = 7 } });

        var inProgress = await SaveAsync(admin, draft.Id, Edited(Original(draftId), p => p["notes"] = "x"));
        var legacy = await SaveAsync(admin, migrated.Id, new JsonObject { ["legacy"] = new JsonObject(), ["amendments"] = new JsonArray() });

        inProgress.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await inProgress.Content.ReadFromJsonAsync<JsonObject>())!["error"]!.GetValue<string>().Should().Be("in-progress");
        legacy.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await legacy.Content.ReadFromJsonAsync<JsonObject>())!["error"]!.GetValue<string>().Should().Be("migrated");
    }

    [Fact]
    public async Task A_reason_is_required()
    {
        var (_, _, test, payload) = await ArrangeAsync("reason");
        var admin = await SuperAdminAsync("reason-admin");

        var res = await SaveAsync(admin, test.Id, Edited(payload, p => p["notes"] = "x"), reason: "   ");

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_amendment_history_can_only_grow_by_this_edits_record()
    {
        var (_, _, test, payload) = await ArrangeAsync("history");
        var admin = await SuperAdminAsync("history-admin");
        var first = await SaveAsync(admin, test.Id, Edited(payload, p => p["notes"] = "First edit"));
        var v2 = (await first.Content.ReadFromJsonAsync<Saved>())!;
        var v2Payload = JsonNode.Parse(await WithDbAsync(db => db.MachineTests.Where(t => t.Id == v2.Id).Select(t => t.PayloadJson!).SingleAsync()))!.AsObject();

        // Version 2's own record rewritten on the way through.
        var tampered = Edited(v2Payload, p => p["amendments"]![0]!["reason"] = "Something more convenient", version: 3);
        var res = await SaveAsync(admin, v2.Id, tampered);

        res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await res.Content.ReadFromJsonAsync<JsonObject>())!["fields"]!.AsArray().Single()!.GetValue<string>().Should().Be("amendments");
    }

    [Fact]
    public async Task A_version_made_by_an_admin_can_itself_be_edited_into_the_next_one()
    {
        var (_, _, test, payload) = await ArrangeAsync("chain");
        var admin = await SuperAdminAsync("chain-admin");
        var v2 = (await (await SaveAsync(admin, test.Id, Edited(payload, p => p["notes"] = "v2"))).Content.ReadFromJsonAsync<Saved>())!;
        var v2Payload = JsonNode.Parse(await WithDbAsync(db => db.MachineTests.Where(t => t.Id == v2.Id).Select(t => t.PayloadJson!).SingleAsync()))!.AsObject();

        var res = await SaveAsync(admin, v2.Id, Edited(v2Payload, p => p["notes"] = "v3", version: 3));

        res.StatusCode.Should().Be(HttpStatusCode.Created, await res.Content.ReadAsStringAsync());
        var v3 = (await res.Content.ReadFromJsonAsync<Saved>())!;
        v3.Version.Should().Be(3);
        var row = await WithDbAsync(db => db.MachineTests.SingleAsync(t => t.Id == v3.Id));
        row.SupersedesClientId.Should().Be(v2.ClientId);
        row.RootClientId.Should().Be(test.ClientId);
        JsonNode.Parse(row.PayloadJson!)!["amendments"]!.AsArray().Should().HaveCount(2);
    }

    // ---- Where the new version shows up -----------------------------------------------------

    [Fact]
    public async Task The_testers_device_pulls_the_admin_version_and_it_names_the_original_it_replaces()
    {
        var (_, _, test, payload) = await ArrangeAsync("pull");
        var admin = await SuperAdminAsync("pull-admin");
        var saved = (await (await SaveAsync(admin, test.Id, Edited(payload, p => p["notes"] = "Edited"))).Content.ReadFromJsonAsync<Saved>())!;

        var tester = _factory.CreateClientAs(Roles.Tester, "pull-tester");
        var pull = JsonNode.Parse(await tester.GetStringAsync("/api/sync/tests"))!;

        var pulled = pull["tests"]!.AsArray().Single(t => t!["clientId"]!.GetValue<Guid>() == saved.ClientId)!;
        var local = JsonNode.Parse(pulled["payloadJson"]!.GetValue<string>())!;
        local["supersedesId"]!.GetValue<string>().Should().Be(test.ClientId.ToString(),
            "the device locks its original through this link");
        local["notes"]!.GetValue<string>().Should().Be("Edited");
    }

    [Fact]
    public async Task Lists_show_the_admin_version_in_place_of_the_one_it_replaced()
    {
        var (_, _, test, payload) = await ArrangeAsync("list");
        var admin = await SuperAdminAsync("list-admin");
        var saved = (await (await SaveAsync(admin, test.Id, Edited(payload, p => p["notes"] = "Edited"))).Content.ReadFromJsonAsync<Saved>())!;

        var colleague = _factory.CreateClientAs(Roles.Tester, "list-tester");
        var list = JsonNode.Parse(await colleague.GetStringAsync("/api/tests"))!;

        list["items"]!.AsArray().Select(i => i!["id"]!.GetValue<Guid>()).Should().BeEquivalentTo([saved.Id]);
    }

    [Fact]
    public async Task An_unfinished_tester_version_is_recorded_as_a_pending_conflict_when_an_admin_saves()
    {
        var (companyId, farmId, test, payload) = await ArrangeAsync("pending");
        var admin = await SuperAdminAsync("pending-admin");
        // The tester reopened version 1 and their draft is on the server, not yet signed off.
        var draftId = Guid.NewGuid();
        await SeedTestAsync(Services, "pending-tester", farmId, companyId, Original(draftId), complete: false,
            version: 2, clientId: draftId, supersedes: test.ClientId, root: test.ClientId);

        var res = await SaveAsync(admin, test.Id, Edited(payload, p => p["notes"] = "Admin edit"));

        res.StatusCode.Should().Be(HttpStatusCode.Created, "a draft isn't a version yet, so version 1 is still current");
        var saved = (await res.Content.ReadFromJsonAsync<Saved>())!;
        var conflict = await WithDbAsync(db => db.SyncConflicts.SingleAsync(c => c.IncomingClientId == draftId));
        conflict.Status.Should().Be(SyncConflictStatus.Pending);
        conflict.DetectedOn.Should().Be(SyncConflictSource.Save);
        conflict.BaseClientId.Should().Be(test.ClientId!.Value);
        conflict.HeadClientId.Should().Be(saved.ClientId);
        conflict.TesterId.Should().Be("pending-tester");
    }

    // ---- What the viewer is told ----------------------------------------------------------

    private sealed record View(Guid Id, int Version, string? EditScope, string? EditBlocked, Guid? LatestId, int? LatestVersion, JsonObject? Draft);

    [Fact]
    public async Task The_viewer_says_how_far_each_role_may_edit_and_why_not()
    {
        var (_, _, test, payload) = await ArrangeAsync("view");
        var superAdmin = await SuperAdminAsync("view-admin");
        var companyAdmin = _factory.CreateClientAs(Roles.CompanyAdministrator, "view-coadmin");
        var tester = _factory.CreateClientAs(Roles.Tester, "view-tester");

        (await superAdmin.GetFromJsonAsync<View>($"/api/tests/{test.Id}"))!.EditScope.Should().Be("full");
        (await companyAdmin.GetFromJsonAsync<View>($"/api/tests/{test.Id}"))!.EditScope.Should().Be("summary");
        (await tester.GetFromJsonAsync<View>($"/api/tests/{test.Id}"))!.EditScope.Should().BeNull();

        var saved = (await (await SaveAsync(superAdmin, test.Id, Edited(payload, p => p["notes"] = "x"))).Content.ReadFromJsonAsync<Saved>())!;
        var original = (await superAdmin.GetFromJsonAsync<View>($"/api/tests/{test.Id}"))!;
        original.EditScope.Should().BeNull();
        original.EditBlocked.Should().Be("superseded");
        original.LatestId.Should().Be(saved.Id);
        original.LatestVersion.Should().Be(2);
        (await superAdmin.GetFromJsonAsync<View>($"/api/tests/{saved.Id}"))!.Version.Should().Be(2);
    }
}
