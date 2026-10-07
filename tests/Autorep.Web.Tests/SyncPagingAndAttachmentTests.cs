using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Autorep.Web.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Autorep.Web.Tests;

// Phase 4 of the offline tester app (plans/offline-tester-app.md): a device's first pull comes in
// pages, newest first, and can leave the pulsation analyser PDFs on the server; a device that let
// its copy of a PDF go can re-send the test without it, and fetch the bytes back to print.
public class SyncPagingAndAttachmentTests : IClassFixture<AuthedWebAppFactory>
{
    private readonly AuthedWebAppFactory _factory;
    public SyncPagingAndAttachmentTests(AuthedWebAppFactory factory) => _factory = factory;

    private sealed record Summary(Guid ClientId, DateTimeOffset CreatedAt, string? PayloadJson);
    private sealed record Page(DateTimeOffset Watermark, List<Summary> Tests, string? Next);

    private static readonly string PdfBase64 = Convert.ToBase64String("%PDF-1.4 analyser export"u8.ToArray());

    private static string PayloadWith(object? pulsationPdf, string farm = "Pāuatahanui Farm") =>
        JsonSerializer.Serialize(new { farmName = farm, readings = new { }, pulsationPdf });

    /// <summary>Tests for <paramref name="testerId"/>, created at the given offsets (minutes ago).</summary>
    private async Task<List<Guid>> SeedTestsAsync(string testerId, params int[] minutesAgo)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
        var farm = new Farm { Name = "Paging Farm " + testerId };
        db.Farms.Add(farm);
        var now = DateTimeOffset.UtcNow;
        var ids = new List<Guid>();
        foreach (var m in minutesAgo)
        {
            var id = Guid.NewGuid();
            ids.Add(id);
            db.MachineTests.Add(new MachineTest
            {
                ClientId = id, TesterId = testerId, FarmId = farm.Id,
                CreatedAt = now.AddMinutes(-m), UpdatedAt = now.AddMinutes(-m),
            });
        }
        await db.SaveChangesAsync();
        return ids;
    }

    private static async Task<Page> GetPageAsync(HttpClient client, string query)
    {
        var res = await client.GetAsync("/api/sync/tests" + query);
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<Page>())!;
    }

    [Fact]
    public async Task A_paged_pull_delivers_every_test_once_newest_first()
    {
        // Two share a timestamp — the migrated-data shape — so the order needs its tie-break.
        var ids = await SeedTestsAsync("tester-page-1", 10, 20, 30, 30, 40, 50, 60);
        var client = _factory.CreateClientAs(Roles.Tester, "tester-page-1");

        var seen = new List<Summary>();
        var pages = 0;
        string? cursor = null;
        DateTimeOffset? firstWatermark = null;
        do
        {
            var page = await GetPageAsync(client, "?limit=3" + (cursor is null ? "" : $"&cursor={cursor}"));
            firstWatermark ??= page.Watermark;
            page.Tests.Count.Should().BeLessThanOrEqualTo(3);
            seen.AddRange(page.Tests);
            cursor = page.Next;
            pages++;
        } while (cursor is not null && pages < 10);

        pages.Should().Be(3);
        seen.Select(t => t.ClientId).Should().BeEquivalentTo(ids).And.OnlyHaveUniqueItems();
        seen.Select(t => t.CreatedAt).Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task Without_a_limit_the_pull_is_the_whole_set_as_older_devices_expect()
    {
        var ids = await SeedTestsAsync("tester-page-2", 1, 2, 3, 4);
        var client = _factory.CreateClientAs(Roles.Tester, "tester-page-2");

        var page = await GetPageAsync(client, "");

        page.Tests.Select(t => t.ClientId).Should().BeEquivalentTo(ids);
        page.Next.Should().BeNull();
    }

    [Fact]
    public async Task An_unrecognised_cursor_is_a_400_so_the_device_starts_again()
    {
        var client = _factory.CreateClientAs(Roles.Tester, "tester-page-3");

        (await client.GetAsync("/api/sync/tests?limit=5&cursor=not-a-cursor")).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync("/api/sync/tests?limit=5&cursor=-1")).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_page_never_exceeds_the_cap_whatever_the_device_asks()
    {
        await SeedTestsAsync("tester-page-4", 1, 2, 3);
        var client = _factory.CreateClientAs(Roles.Tester, "tester-page-4");

        var page = await GetPageAsync(client, "?limit=0");

        page.Tests.Should().HaveCount(1); // clamped up to the minimum of one
        page.Next.Should().Be("1");
    }

    private async Task<HttpResponseMessage> PushAsync(HttpClient client, Guid clientId, string payloadJson, Guid? supersedes = null)
    {
        var res = await client.PostAsJsonAsync("/api/sync/tests", new
        {
            clientId,
            farmName = "Attachment Farm",
            createdAt = DateTimeOffset.UtcNow,
            payloadJson,
            version = supersedes is null ? 1 : 2,
            supersedesClientId = supersedes,
        });
        res.IsSuccessStatusCode.Should().BeTrue(await res.Content.ReadAsStringAsync());
        return res;
    }

    private async Task<string?> StoredPayloadAsync(string testerId, Guid clientId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
        return await db.MachineTests.Where(t => t.TesterId == testerId && t.ClientId == clientId)
            .Select(t => t.PayloadJson).SingleAsync();
    }

    [Fact]
    public async Task A_pull_can_leave_the_analyser_pdf_on_the_server()
    {
        var client = _factory.CreateClientAs(Roles.Tester, "tester-attach-1");
        var id = Guid.NewGuid();
        await PushAsync(client, id, PayloadWith(new { name = "pulse.pdf", base64 = PdfBase64, size = 24, attachedAt = "2026-10-01T00:00:00Z" }));

        var lean = (await GetPageAsync(client, "?limit=10&attachments=omit")).Tests.Single(t => t.ClientId == id);
        var full = (await GetPageAsync(client, "?limit=10")).Tests.Single(t => t.ClientId == id);

        var leanPdf = JsonNode.Parse(lean.PayloadJson!)!["pulsationPdf"]!;
        leanPdf["base64"].Should().BeNull();
        leanPdf["onServer"]!.GetValue<bool>().Should().BeTrue();
        leanPdf["name"]!.GetValue<string>().Should().Be("pulse.pdf");
        lean.PayloadJson.Should().Contain("Pāuatahanui", "rewriting a payload must not escape its text");
        JsonNode.Parse(full.PayloadJson!)!["pulsationPdf"]!["base64"]!.GetValue<string>().Should().Be(PdfBase64);
    }

    [Fact]
    public async Task Re_sending_a_test_without_its_pdf_bytes_keeps_the_bytes_already_stored()
    {
        var client = _factory.CreateClientAs(Roles.Tester, "tester-attach-2");
        var id = Guid.NewGuid();
        await PushAsync(client, id, PayloadWith(new { name = "pulse.pdf", base64 = PdfBase64, size = 24, attachedAt = "2026-10-01T00:00:00Z" }));

        // The device let its copy go, then something made the test dirty and it was pushed again.
        await PushAsync(client, id, PayloadWith(new { name = "pulse.pdf", size = 24, attachedAt = "2026-10-01T00:00:00Z", onServer = true }));

        PulsationPayload.Base64(await StoredPayloadAsync("tester-attach-2", id)).Should().Be(PdfBase64);
    }

    [Fact]
    public async Task A_new_version_pointing_at_the_originals_pdf_gets_the_bytes()
    {
        var client = _factory.CreateClientAs(Roles.Tester, "tester-attach-3");
        var original = Guid.NewGuid();
        await PushAsync(client, original, PayloadWith(new { name = "pulse.pdf", base64 = PdfBase64, size = 24, attachedAt = "2026-10-01T00:00:00Z" }));

        var named = Guid.NewGuid();
        await PushAsync(client, named,
            PayloadWith(new { name = "pulse.pdf", size = 24, attachedAt = "2026-10-01T00:00:00Z", onServer = true, serverTestId = original }),
            supersedes: original);
        // A pointer that names no test still finds the bytes through the version chain.
        var unnamed = Guid.NewGuid();
        await PushAsync(client, unnamed,
            PayloadWith(new { name = "pulse.pdf", size = 24, attachedAt = "2026-10-01T00:00:00Z", onServer = true }),
            supersedes: original);

        PulsationPayload.Base64(await StoredPayloadAsync("tester-attach-3", named)).Should().Be(PdfBase64);
        PulsationPayload.Base64(await StoredPayloadAsync("tester-attach-3", unnamed)).Should().Be(PdfBase64);
    }

    // Codex review of #76: two devices, one test. Device X let its copy of PDF A go; device Y has
    // since attached PDF B and synced. X's stale pointer must not get B's bytes under A's name — the
    // report would print one analyser's results labelled as another's.
    [Fact]
    public async Task A_stale_pointer_never_gets_another_attachments_bytes()
    {
        var client = _factory.CreateClientAs(Roles.Tester, "tester-attach-6");
        var id = Guid.NewGuid();
        var otherBytes = Convert.ToBase64String("%PDF-1.4 a different export"u8.ToArray());
        await PushAsync(client, id, PayloadWith(new { name = "pulse.pdf", base64 = otherBytes, size = 27, attachedAt = "2026-10-05T00:00:00Z" }));

        await PushAsync(client, id, PayloadWith(new { name = "pulse.pdf", size = 24, attachedAt = "2026-10-01T00:00:00Z", onServer = true }));

        var stored = await StoredPayloadAsync("tester-attach-6", id);
        PulsationPayload.Base64(stored).Should().BeNull("B's bytes must not be filed under A's name");
        PulsationPayload.IsServerPointer(stored, out _).Should().BeTrue();
    }

    [Fact]
    public async Task The_pdf_comes_back_on_demand_but_only_to_the_tester_whose_test_it_is()
    {
        var mine = _factory.CreateClientAs(Roles.Tester, "tester-attach-4");
        var id = Guid.NewGuid();
        await PushAsync(mine, id, PayloadWith(new { name = "pulse.pdf", base64 = PdfBase64, size = 24, attachedAt = "2026-10-01T00:00:00Z" }));

        var res = await mine.GetAsync($"/api/sync/tests/{id}/pulsation-pdf");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        res.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        (await res.Content.ReadAsByteArrayAsync()).Should().Equal(Convert.FromBase64String(PdfBase64));
        res.Headers.CacheControl!.NoStore.Should().BeTrue();

        var someoneElse = _factory.CreateClientAs(Roles.Tester, "tester-attach-5");
        (await someoneElse.GetAsync($"/api/sync/tests/{id}/pulsation-pdf")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

// The payload helpers on their own: a payload is the device's, and the server must never be what
// breaks one.
public class PulsationPayloadTests
{
    private const string Bytes = "JVBERi0xLjQ=";

    [Fact]
    public void Leaves_a_payload_without_an_attachment_or_that_is_not_json_alone()
    {
        PulsationPayload.WithoutBytes(null).Should().BeNull();
        PulsationPayload.WithoutBytes("not json").Should().Be("not json");
        PulsationPayload.WithoutBytes("""{"readings":{}}""").Should().Be("""{"readings":{}}""");
        PulsationPayload.Base64("""{"pulsationPdf":null}""").Should().BeNull();
        PulsationPayload.IsServerPointer("""{"pulsationPdf":{"name":"a.pdf"}}""", out _).Should().BeFalse();
    }

    [Fact]
    public void Takes_the_bytes_out_and_puts_them_back()
    {
        var payload = $$$"""{"farmName":"Ōtorohanga","pulsationPdf":{"name":"a.pdf","base64":"{{{Bytes}}}","size":9}}""";

        var lean = PulsationPayload.WithoutBytes(payload)!;
        PulsationPayload.Base64(lean).Should().BeNull();
        PulsationPayload.IsServerPointer(lean, out var source).Should().BeTrue();
        source.Should().BeNull();
        lean.Should().Contain("Ōtorohanga");

        var restored = PulsationPayload.WithBytes(lean, Bytes);
        PulsationPayload.Base64(restored).Should().Be(Bytes);
        PulsationPayload.IsServerPointer(restored, out _).Should().BeFalse();
    }

    [Fact]
    public void Reads_which_test_holds_the_bytes()
    {
        var id = Guid.NewGuid();
        PulsationPayload.IsServerPointer($$$"""{"pulsationPdf":{"name":"a.pdf","onServer":true,"serverTestId":"{{{id}}}"}}""", out var source)
            .Should().BeTrue();
        source.Should().Be(id);
    }
}
