using System.Net;
using System.Text.Json;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Autorep.Web.Services;
using Autorep.Web.Services.Pdfs;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Autorep.Web.Tests;

// The backfill moves analyser PDFs written before the store (or while it was down) out of
// PayloadJson. A dry run changes nothing; a run moves them, audited; running again finds nothing;
// and a test pushed again mid-pass is never overwritten with its older payload.
public class PulsationBackfillTests : IClassFixture<AuthedWebAppFactory>
{
    private readonly AuthedWebAppFactory _factory;
    public PulsationBackfillTests(AuthedWebAppFactory factory) => _factory = factory;

    private InMemoryPdfStore Store => _factory.Services.GetRequiredService<InMemoryPdfStore>();

    private static string Inline(byte[] pdf, string name = "pulse.pdf") =>
        JsonSerializer.Serialize(new
        {
            farmName = "Ōhaupō Farm",
            pulsationPdf = new { name, base64 = Convert.ToBase64String(pdf), size = pdf.Length, attachedAt = "2026-09-01T00:00:00Z" },
        });

    private async Task<T> WithDbAsync<T>(Func<AutorepDbContext, Task<T>> f)
    {
        using var scope = _factory.Services.CreateScope();
        return await f(scope.ServiceProvider.GetRequiredService<AutorepDbContext>());
    }

    private Task<MachineTest> SeedAsync(string testerId, string? payload, DateTimeOffset? updatedAt = null) =>
        WithDbAsync(async db =>
        {
            var farm = new Farm { Name = "Backfill Farm" };
            db.Farms.Add(farm);
            var test = new MachineTest
            {
                TesterId = testerId, FarmId = farm.Id, ClientId = Guid.NewGuid(), PayloadJson = payload,
                UpdatedAt = updatedAt ?? DateTimeOffset.UtcNow.AddDays(-30),
            };
            db.MachineTests.Add(test);
            await db.SaveChangesAsync();
            return test;
        });

    private Task<MachineTest> ReloadAsync(Guid id) =>
        WithDbAsync(db => db.MachineTests.AsNoTracking().SingleAsync(t => t.Id == id));

    private async Task<PulsationBackfillResult> RunAsync(bool dryRun)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<PulsationBackfill>().RunAsync(dryRun, CancellationToken.None);
    }

    [Fact]
    public async Task A_dry_run_counts_what_would_move_and_moves_nothing_then_a_run_moves_it_once()
    {
        var pdf = "%PDF-1.4 written before the store"u8.ToArray();
        var test = await SeedAsync("bf-tester", Inline(pdf));
        var key = PdfKeys.Pulsation("bf-tester", test.ClientId!.Value, PdfHash.Sha256Hex(pdf));

        var dry = await RunAsync(dryRun: true);

        dry.WouldMove.Should().BeGreaterThanOrEqualTo(1);
        dry.Moved.Should().Be(0);
        (await ReloadAsync(test.Id)).PayloadJson.Should().Be(test.PayloadJson, "a dry run writes nothing");
        (await Store.ExistsAsync(PdfContainer.PulsationData, key)).Should().BeFalse();

        var run = await RunAsync(dryRun: false);

        run.Moved.Should().BeGreaterThanOrEqualTo(1);
        var moved = await ReloadAsync(test.Id);
        PulsationPayload.Base64(moved.PayloadJson).Should().BeNull();
        PulsationPayload.StoredSha256(moved.PayloadJson).Should().Be(PdfHash.Sha256Hex(pdf));
        moved.PayloadJson.Should().Contain("Ōhaupō", "only the attachment changes; the payload's text is the device's");
        moved.UpdatedAt.Should().Be(test.UpdatedAt, "nothing about the test changed, so no device need pull it again");
        (await Store.GetAsync(PdfContainer.PulsationData, key))!.Bytes.Should().Equal(pdf);
        var audit = await WithDbAsync(db => db.AuditEntries.SingleAsync(a => a.EntityKey == test.Id.ToString() && a.Operation == "PulsationPdfMovedToStore"));
        audit.AfterJson.Should().Contain(PdfHash.Sha256Hex(pdf));

        var again = await RunAsync(dryRun: false);

        again.Moved.Should().Be(0, "there's nothing left inline to move");
        (await ReloadAsync(test.Id)).PayloadJson.Should().Be(moved.PayloadJson);
        (await WithDbAsync(db => db.AuditEntries.CountAsync(a => a.EntityKey == test.Id.ToString() && a.Operation == "PulsationPdfMovedToStore")))
            .Should().Be(1);
    }

    [Fact]
    public async Task After_the_move_the_tester_still_gets_the_pdf_back()
    {
        var pdf = "%PDF-1.4 moved and fetched back"u8.ToArray();
        var test = await SeedAsync("bf-fetch", Inline(pdf));
        await RunAsync(dryRun: false);

        var res = await _factory.CreateClientAs(Roles.Tester, "bf-fetch").GetAsync($"/api/sync/tests/{test.ClientId}/pulsation-pdf");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.Content.ReadAsByteArrayAsync()).Should().Equal(pdf);
    }

    [Fact]
    public async Task A_test_pushed_again_mid_pass_is_never_overwritten_with_its_older_payload()
    {
        var pdf = "%PDF-1.4 raced"u8.ToArray();
        var test = await SeedAsync("bf-race", Inline(pdf));
        // What the pass read, before the push below landed.
        var seen = new PulsationBackfill.Candidate(test.Id, test.TesterId, test.ClientId, test.UpdatedAt, test.PayloadJson!);
        var pushed = """{"farmName":"Ōhaupō Farm","readings":{"1a":49}}""";
        await WithDbAsync(async db =>
        {
            var row = await db.MachineTests.SingleAsync(t => t.Id == test.Id);
            row.PayloadJson = pushed;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            return await db.SaveChangesAsync();
        });

        using var scope = _factory.Services.CreateScope();
        var (outcome, _) = await scope.ServiceProvider.GetRequiredService<PulsationBackfill>().MoveAsync(seen, dryRun: false, CancellationToken.None);

        outcome.Should().Be(PulsationBackfill.Outcome.ChangedMeanwhile);
        (await ReloadAsync(test.Id)).PayloadJson.Should().Be(pushed);
    }

    [Fact]
    public async Task Bytes_that_arent_base64_are_left_exactly_as_the_device_sent_them()
    {
        var payload = """{"pulsationPdf":{"name":"odd.pdf","base64":"!!! not base64 !!!","size":3,"attachedAt":"2026-09-01T00:00:00Z"}}""";
        var test = await SeedAsync("bf-odd", payload);

        var result = await RunAsync(dryRun: false);

        result.Unreadable.Should().BeGreaterThanOrEqualTo(1);
        (await ReloadAsync(test.Id)).PayloadJson.Should().Be(payload);
    }

    [Fact]
    public async Task With_the_store_down_the_pass_stops_and_changes_nothing()
    {
        var test = await SeedAsync("bf-down", Inline("%PDF-1.4 store down"u8.ToArray()));
        Store.Unavailable = true;
        PulsationBackfillResult result;
        try
        {
            result = await RunAsync(dryRun: false);
        }
        finally
        {
            Store.Unavailable = false;
        }

        result.Failed.Should().Be(1);
        (await ReloadAsync(test.Id)).PayloadJson.Should().Be(test.PayloadJson);
    }

    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Autorep.Web";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static IConfiguration Config(string? mode) =>
        new ConfigurationBuilder().AddInMemoryCollection(
            mode is null ? [] : [new KeyValuePair<string, string?>("PdfStore:PulsationBackfill", mode)]).Build();

    [Theory]
    [InlineData("Testing", null, PulsationBackfillMode.Off)]
    [InlineData("Staging", null, PulsationBackfillMode.DryRun)]
    [InlineData("Production", null, PulsationBackfillMode.DryRun)]
    [InlineData("Staging", "Run", PulsationBackfillMode.Run)]
    [InlineData("Production", "run", PulsationBackfillMode.Run)]
    [InlineData("Staging", "Off", PulsationBackfillMode.Off)]
    public void Reads_only_unless_told_to_run(string environment, string? setting, PulsationBackfillMode expected) =>
        PulsationBackfillService.ModeFor(Config(setting), new Env(environment)).Should().Be(expected);

    [Fact]
    public void A_typo_in_the_setting_doesnt_stop_the_app() =>
        PulsationBackfillService.ModeFor(Config("Yes please"), new Env("Production")).Should().BeNull();
}
