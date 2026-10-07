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

namespace Autorep.Web.Tests;

// The pulsation analyser PDFs live in the PDF store, not in PayloadJson: a push moves them there and
// leaves the pointer a device already understands; a pull that doesn't ask for attachments=omit
// still gets them inline (anything older keeps working); the tester's route and the read-only view's
// route read them back; and the store being down never fails a sync.
public class PulsationStoreTests : IClassFixture<AuthedWebAppFactory>
{
    private readonly AuthedWebAppFactory _factory;
    public PulsationStoreTests(AuthedWebAppFactory factory) => _factory = factory;

    private static readonly byte[] Pdf = "%PDF-1.4 pulsation analyser export"u8.ToArray();
    private static readonly string Base64 = Convert.ToBase64String(Pdf);
    private static readonly string Sha = PdfHash.Sha256Hex(Pdf);

    private InMemoryPdfStore Store => _factory.Services.GetRequiredService<InMemoryPdfStore>();

    private static string PayloadWith(object? pulsationPdf) =>
        JsonSerializer.Serialize(new { farmName = "Pāuatahanui Farm", readings = new { }, pulsationPdf });

    private static object Attached(string? base64 = null, bool onServer = false, string? sha256 = null) =>
        new { name = "pulse.pdf", base64, size = Pdf.Length, attachedAt = "2026-10-01T00:00:00Z", onServer = onServer ? true : (bool?)null, sha256 };

    private async Task<T> WithDbAsync<T>(Func<AutorepDbContext, Task<T>> f)
    {
        using var scope = _factory.Services.CreateScope();
        return await f(scope.ServiceProvider.GetRequiredService<AutorepDbContext>());
    }

    private static async Task PushAsync(HttpClient client, Guid clientId, string payloadJson, bool complete = false)
    {
        var res = await client.PostAsJsonAsync("/api/sync/tests", new
        {
            clientId,
            farmName = "Attachment Farm",
            createdAt = DateTimeOffset.UtcNow,
            markedCompleteAt = complete ? DateTimeOffset.UtcNow : (DateTimeOffset?)null,
            payloadJson,
        });
        res.IsSuccessStatusCode.Should().BeTrue(await res.Content.ReadAsStringAsync());
    }

    private Task<MachineTest> RowAsync(string testerId, Guid clientId) =>
        WithDbAsync(db => db.MachineTests.AsNoTracking().SingleAsync(t => t.TesterId == testerId && t.ClientId == clientId));

    private sealed record Summary(Guid ClientId, string? PayloadJson);
    private sealed record PageDto(List<Summary> Tests);

    private static JsonNode? AttachmentIn(string? payload) => JsonNode.Parse(payload!)!["pulsationPdf"];

    // ---- Push ---------------------------------------------------------------

    [Fact]
    public async Task A_push_moves_the_pdf_into_the_store_and_leaves_the_pointer_devices_understand()
    {
        var client = _factory.CreateClientAs(Roles.Tester, "ps-push");
        var id = Guid.NewGuid();

        await PushAsync(client, id, PayloadWith(Attached(Base64)));

        var row = await RowAsync("ps-push", id);
        row.PayloadJson.Should().NotContain(Base64, "the bytes are no longer in the database");
        row.PayloadJson.Should().Contain("Pāuatahanui", "the rest of the payload is the device's text, untouched");
        var attachment = AttachmentIn(row.PayloadJson)!;
        attachment["onServer"]!.GetValue<bool>().Should().BeTrue();
        attachment["sha256"]!.GetValue<string>().Should().Be(Sha);
        attachment["serverTestId"].Should().BeNull("this test holds its own copy");
        attachment["name"]!.GetValue<string>().Should().Be("pulse.pdf");
        (await Store.GetAsync(PdfContainer.PulsationData, PdfKeys.Pulsation("ps-push", id, Sha)))!.Bytes.Should().Equal(Pdf);
    }

    [Fact]
    public async Task Sending_the_same_pdf_again_stores_nothing_new()
    {
        var client = _factory.CreateClientAs(Roles.Tester, "ps-again");
        var id = Guid.NewGuid();
        await PushAsync(client, id, PayloadWith(Attached(Base64)));
        await PushAsync(client, id, PayloadWith(Attached(Base64)));

        Store.Keys(PdfContainer.PulsationData).Where(k => k.StartsWith("ps-again/", StringComparison.Ordinal)).Should().ContainSingle();
    }

    [Fact]
    public async Task With_the_store_down_a_push_still_lands_and_keeps_the_pdf_inline()
    {
        var client = _factory.CreateClientAs(Roles.Tester, "ps-down");
        var id = Guid.NewGuid();
        Store.Unavailable = true;
        try
        {
            await PushAsync(client, id, PayloadWith(Attached(Base64)));
        }
        finally
        {
            Store.Unavailable = false;
        }

        PulsationPayload.Base64((await RowAsync("ps-down", id)).PayloadJson).Should().Be(Base64,
            "a tester's sync never depends on the store; the backfill moves it later");
        (await client.GetAsync($"/api/sync/tests/{id}/pulsation-pdf")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_pointer_carrying_a_hash_the_server_cant_match_is_kept_without_it()
    {
        var client = _factory.CreateClientAs(Roles.Tester, "ps-forged");
        var id = Guid.NewGuid();

        await PushAsync(client, id, PayloadWith(Attached(onServer: true, sha256: new string('f', 64))));

        PulsationPayload.StoredSha256((await RowAsync("ps-forged", id)).PayloadJson).Should().BeNull();
    }

    // ---- Pull ---------------------------------------------------------------

    [Fact]
    public async Task A_pull_without_omit_still_gets_the_bytes_inline_from_the_store()
    {
        var client = _factory.CreateClientAs(Roles.Tester, "ps-pull");
        var id = Guid.NewGuid();
        await PushAsync(client, id, PayloadWith(Attached(Base64)));

        var full = (await client.GetFromJsonAsync<PageDto>("/api/sync/tests"))!.Tests.Single(t => t.ClientId == id);
        var lean = (await client.GetFromJsonAsync<PageDto>("/api/sync/tests?attachments=omit&limit=50"))!.Tests.Single(t => t.ClientId == id);
        var single = await client.GetFromJsonAsync<Summary>($"/api/sync/tests/{(await RowAsync("ps-pull", id)).Id}");

        AttachmentIn(full.PayloadJson)!["base64"]!.GetValue<string>().Should().Be(Base64);
        AttachmentIn(full.PayloadJson)!["onServer"].Should().BeNull("an older device gets the shape it always got");
        AttachmentIn(full.PayloadJson)!["sha256"].Should().BeNull();
        AttachmentIn(lean.PayloadJson)!["base64"].Should().BeNull();
        AttachmentIn(lean.PayloadJson)!["onServer"]!.GetValue<bool>().Should().BeTrue();
        AttachmentIn(single!.PayloadJson)!["base64"]!.GetValue<string>().Should().Be(Base64);
    }

    [Fact]
    public async Task A_pull_without_omit_with_the_store_down_sends_the_pointer_rather_than_failing()
    {
        var client = _factory.CreateClientAs(Roles.Tester, "ps-pull-down");
        var id = Guid.NewGuid();
        await PushAsync(client, id, PayloadWith(Attached(Base64)));

        Store.Unavailable = true;
        try
        {
            var res = await client.GetAsync("/api/sync/tests");
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var test = (await res.Content.ReadFromJsonAsync<PageDto>())!.Tests.Single(t => t.ClientId == id);
            AttachmentIn(test.PayloadJson)!["onServer"]!.GetValue<bool>().Should().BeTrue();
        }
        finally
        {
            Store.Unavailable = false;
        }
    }

    [Fact]
    public async Task The_testers_route_says_try_later_when_the_store_is_down_not_that_there_is_no_pdf()
    {
        var client = _factory.CreateClientAs(Roles.Tester, "ps-route-down");
        var id = Guid.NewGuid();
        await PushAsync(client, id, PayloadWith(Attached(Base64)));

        Store.Unavailable = true;
        try
        {
            (await client.GetAsync($"/api/sync/tests/{id}/pulsation-pdf")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        }
        finally
        {
            Store.Unavailable = false;
        }
    }

    // ---- The read-only view -------------------------------------------------

    private async Task<(Guid CompanyId, Guid TestId)> SeedCompanyTestAsync(string testerId, string payload)
    {
        return await WithDbAsync(async db =>
        {
            var company = new TestingCompany { Name = $"PS Co {Guid.NewGuid()}" };
            db.TestingCompanies.Add(company);
            db.Users.Add(new Tester { Id = testerId, UserName = testerId, DisplayName = testerId, TestingCompanyId = company.Id });
            var farm = new Farm { Name = "PS Farm", CreatedByTestingCompanyId = company.Id };
            db.Farms.Add(farm);
            var test = new MachineTest
            {
                TesterId = testerId, FarmId = farm.Id, TestingCompanyId = company.Id, ClientId = Guid.NewGuid(),
                MarkedCompleteAt = DateTimeOffset.UtcNow, PayloadJson = payload,
            };
            db.MachineTests.Add(test);
            await db.SaveChangesAsync();
            return (company.Id, test.Id);
        });
    }

    private async Task<Guid> SeedUserAsync(string id, Guid? companyId) =>
        await WithDbAsync(async db =>
        {
            db.Users.Add(new Tester { Id = id, UserName = id, DisplayName = id, TestingCompanyId = companyId });
            await db.SaveChangesAsync();
            return Guid.Empty;
        });

    [Fact]
    public async Task The_views_route_hands_the_pdf_to_whoever_can_view_the_test_and_no_one_else()
    {
        var (companyId, testId) = await SeedCompanyTestAsync("ps-owner", PayloadWith(Attached(Base64)));
        // The owner's row was seeded inline, as a test from before the store; the route reads either.
        await SeedUserAsync("ps-company-admin", companyId);
        await SeedUserAsync("ps-colleague", companyId);
        await SeedUserAsync("ps-other-admin", null);
        await SeedUserAsync("ps-outsider", Guid.NewGuid());
        var url = $"/api/tests/{testId}/pulsation-pdf";

        var super = await _factory.CreateClientAs(Roles.SuperAdministrator, "ps-super").GetAsync(url);
        super.StatusCode.Should().Be(HttpStatusCode.OK);
        (await super.Content.ReadAsByteArrayAsync()).Should().Equal(Pdf);
        super.Headers.CacheControl!.NoStore.Should().BeTrue();
        (await _factory.CreateClientAs(Roles.CompanyAdministrator, "ps-company-admin").GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _factory.CreateClientAs(Roles.Tester, "ps-colleague").GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _factory.CreateClientAs(Roles.CompanyAdministrator, "ps-other-admin").GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _factory.CreateClientAs(Roles.Tester, "ps-outsider").GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_views_route_reads_a_stored_copy_through_its_holder()
    {
        // A pointer naming the test that holds the bytes — a new version made from an original.
        var holder = Guid.NewGuid();
        await Store.PutAsync(PdfContainer.PulsationData, PdfKeys.Pulsation("ps-holder", holder, Sha), Pdf, "application/pdf");
        var (_, testId) = await SeedCompanyTestAsync("ps-holder", PulsationPayload.AsStored(PayloadWith(Attached()), Sha, holder)!);

        var res = await _factory.CreateClientAs(Roles.SuperAdministrator, "ps-super-2").GetAsync($"/api/tests/{testId}/pulsation-pdf");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.Content.ReadAsByteArrayAsync()).Should().Equal(Pdf);
    }

    [Fact]
    public async Task The_view_gets_a_pointer_when_it_asks_and_the_bytes_inline_when_it_doesnt()
    {
        var tester = _factory.CreateClientAs(Roles.Tester, "ps-view");
        var id = Guid.NewGuid();
        await SeedUserAsync("ps-view", null);
        await PushAsync(tester, id, PayloadWith(Attached(Base64)), complete: true);
        var testId = (await RowAsync("ps-view", id)).Id;
        var admin = _factory.CreateClientAs(Roles.SuperAdministrator, "ps-super-3");

        var lean = await admin.GetFromJsonAsync<Summary>($"/api/tests/{testId}?attachments=omit");
        var full = await admin.GetFromJsonAsync<Summary>($"/api/tests/{testId}");

        AttachmentIn(lean!.PayloadJson)!["base64"].Should().BeNull();
        AttachmentIn(lean.PayloadJson)!["onServer"]!.GetValue<bool>().Should().BeTrue();
        AttachmentIn(full!.PayloadJson)!["base64"]!.GetValue<string>().Should().Be(Base64, "a bundle from before this still prints the PDF");
    }

    [Fact]
    public async Task No_attachment_is_a_404_on_both_routes()
    {
        var (_, testId) = await SeedCompanyTestAsync("ps-none", PayloadWith(null));
        var clientId = await WithDbAsync(db => db.MachineTests.Where(t => t.Id == testId).Select(t => t.ClientId!.Value).SingleAsync());

        (await _factory.CreateClientAs(Roles.SuperAdministrator, "ps-super-4").GetAsync($"/api/tests/{testId}/pulsation-pdf"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _factory.CreateClientAs(Roles.Tester, "ps-none").GetAsync($"/api/sync/tests/{clientId}/pulsation-pdf"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
