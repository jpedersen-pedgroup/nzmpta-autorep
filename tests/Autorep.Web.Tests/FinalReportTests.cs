using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Autorep.Web.Api;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Autorep.Web.Services.Pdfs;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Autorep.Web.Tests;

// The Final Report as the tester signed it off (PRD: FinalReportBlob): a tester's device sends it
// for its own signed-off tests only (PUT /api/sync/final-report/{clientId}), and whoever can view the
// test can download it (GET /api/tests/{id}/final-report), with the same company scoping as the view.
public class FinalReportTests : IClassFixture<AuthedWebAppFactory>
{
    private readonly AuthedWebAppFactory _factory;
    public FinalReportTests(AuthedWebAppFactory factory) => _factory = factory;

    private static readonly byte[] Report = "%PDF-1.7\n% the report as signed off\n%%EOF"u8.ToArray();

    private InMemoryPdfStore Store => _factory.Services.GetRequiredService<InMemoryPdfStore>();

    private async Task<T> WithDbAsync<T>(Func<AutorepDbContext, Task<T>> f)
    {
        using var scope = _factory.Services.CreateScope();
        return await f(scope.ServiceProvider.GetRequiredService<AutorepDbContext>());
    }

    private sealed record Seeded(Guid CompanyId, Guid TestId, Guid ClientId, string TesterId);

    /// <summary>A company, a tester in it, and one of their tests (complete unless told otherwise).</summary>
    private Task<Seeded> SeedAsync(string prefix, bool complete = true, Guid? companyId = null) =>
        WithDbAsync(async db =>
        {
            var company = companyId ?? Guid.NewGuid();
            if (companyId is null) db.TestingCompanies.Add(new TestingCompany { Id = company, Name = $"{prefix} Co {Guid.NewGuid()}" });
            var testerId = $"{prefix}-{Guid.NewGuid():N}";
            db.Users.Add(new Tester { Id = testerId, UserName = testerId, DisplayName = prefix, TestingCompanyId = company });
            var farm = new Farm { Name = $"{prefix} Farm", CreatedByTestingCompanyId = company };
            db.Farms.Add(farm);
            var test = new MachineTest
            {
                TesterId = testerId, FarmId = farm.Id, TestingCompanyId = company, ClientId = Guid.NewGuid(),
                MarkedCompleteAt = complete ? new DateTimeOffset(2026, 10, 6, 21, 30, 0, TimeSpan.Zero) : null,
            };
            db.MachineTests.Add(test);
            await db.SaveChangesAsync();
            return new Seeded(company, test.Id, test.ClientId!.Value, testerId);
        });

    private static ByteArrayContent Pdf(byte[] bytes, string contentType = "application/pdf")
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return content;
    }

    private Task<HttpResponseMessage> UploadAsync(string testerId, Guid clientId, byte[] bytes, string contentType = "application/pdf") =>
        _factory.CreateClientAs(Roles.Tester, testerId).PutAsync($"/api/sync/final-report/{clientId}", Pdf(bytes, contentType));

    private Task<FinalReportBlob?> RecordAsync(Guid testId) =>
        WithDbAsync(db => db.FinalReportBlobs.AsNoTracking().FirstOrDefaultAsync(r => r.MachineTestId == testId));

    // ---- Upload ------------------------------------------------------------

    [Fact]
    public async Task The_device_stores_the_report_for_its_own_signed_off_test()
    {
        var s = await SeedAsync("up");

        var res = await UploadAsync(s.TesterId, s.ClientId, Report);

        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var key = PdfKeys.FinalReport(s.TesterId, s.ClientId);
        (await Store.GetAsync(PdfContainer.FinalReports, key))!.Bytes.Should().Equal(Report);
        var record = await RecordAsync(s.TestId);
        record!.BlobKey.Should().Be(key);
        record.Sha256.Should().Be(PdfHash.Sha256Hex(Report));
        record.SizeBytes.Should().Be(Report.Length);
        record.StoredBy.Should().Be(s.TesterId);
    }

    [Fact]
    public async Task Sending_the_same_report_again_changes_nothing()
    {
        var s = await SeedAsync("same");
        await UploadAsync(s.TesterId, s.ClientId, Report);
        var storedAt = (await RecordAsync(s.TestId))!.StoredAt;
        var audits = await WithDbAsync(db => db.AuditEntries.CountAsync(a => a.EntityType == nameof(FinalReportBlob) && a.EntityKey == s.TestId.ToString()));

        var again = await UploadAsync(s.TesterId, s.ClientId, Report);

        again.StatusCode.Should().Be(HttpStatusCode.OK);
        (await again.Content.ReadFromJsonAsync<FinalReportsController.StoredResponse>())!.Status.Should().Be("unchanged");
        (await RecordAsync(s.TestId))!.StoredAt.Should().Be(storedAt);
        (await WithDbAsync(db => db.AuditEntries.CountAsync(a => a.EntityType == nameof(FinalReportBlob) && a.EntityKey == s.TestId.ToString())))
            .Should().Be(audits, "nothing was written, so nothing is audited");
    }

    [Fact]
    public async Task A_different_report_replaces_it_and_the_record_follows()
    {
        var s = await SeedAsync("replace");
        await UploadAsync(s.TesterId, s.ClientId, Report);
        var revised = "%PDF-1.7\n% made again after the logo changed\n%%EOF"u8.ToArray();

        var res = await UploadAsync(s.TesterId, s.ClientId, revised);

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Store.GetAsync(PdfContainer.FinalReports, PdfKeys.FinalReport(s.TesterId, s.ClientId)))!.Bytes.Should().Equal(revised);
        (await RecordAsync(s.TestId))!.Sha256.Should().Be(PdfHash.Sha256Hex(revised));
    }

    [Fact]
    public async Task Upload_is_audited()
    {
        var s = await SeedAsync("audit");

        await UploadAsync(s.TesterId, s.ClientId, Report);

        var audit = await WithDbAsync(db => db.AuditEntries.SingleAsync(a => a.EntityType == nameof(FinalReportBlob) && a.EntityKey == s.TestId.ToString()));
        audit.Actor.Should().Be(s.TesterId);
        audit.AfterJson.Should().Contain(PdfHash.Sha256Hex(Report), "the hash identifies the copy for seven years, even if the blob is lost");
    }

    [Fact]
    public async Task Another_testers_test_reads_as_not_found_and_nothing_is_stored()
    {
        var owner = await SeedAsync("owner");
        var intruder = await SeedAsync("intruder");

        var res = await UploadAsync(intruder.TesterId, owner.ClientId, Report);

        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await RecordAsync(owner.TestId)).Should().BeNull();
        Store.Keys(PdfContainer.FinalReports).Should().NotContain(k => k.Contains(owner.ClientId.ToString()));
    }

    [Fact]
    public async Task A_test_the_server_does_not_have_as_complete_is_a_conflict()
    {
        var s = await SeedAsync("draft", complete: false);

        var res = await UploadAsync(s.TesterId, s.ClientId, Report);

        res.StatusCode.Should().Be(HttpStatusCode.Conflict, "the device retries after the completed test has gone up");
        (await RecordAsync(s.TestId)).Should().BeNull();
    }

    [Fact]
    public async Task Only_a_raw_pdf_body_is_accepted()
    {
        var s = await SeedAsync("types");

        (await UploadAsync(s.TesterId, s.ClientId, Report, "application/json")).StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        (await UploadAsync(s.TesterId, s.ClientId, "<!DOCTYPE html><p>a sign-in page</p>"u8.ToArray())).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await RecordAsync(s.TestId)).Should().BeNull();
    }

    [Fact]
    public async Task A_report_over_the_limit_is_refused_whether_or_not_it_says_how_big_it_is()
    {
        var s = await SeedAsync("big");
        var tooBig = new byte[FinalReportsController.MaxBytes + 1];
        "%PDF-1.7"u8.CopyTo(tooBig);
        var client = _factory.CreateClientAs(Roles.Tester, s.TesterId);

        (await client.PutAsync($"/api/sync/final-report/{s.ClientId}", Pdf(tooBig))).StatusCode
            .Should().Be(HttpStatusCode.RequestEntityTooLarge);

        // No Content-Length: chunked, so it is only found out while reading.
        var streamed = new StreamContent(new UnknownLengthStream(tooBig));
        streamed.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        (await client.PutAsync($"/api/sync/final-report/{s.ClientId}", streamed)).StatusCode
            .Should().Be(HttpStatusCode.RequestEntityTooLarge);

        (await RecordAsync(s.TestId)).Should().BeNull();
    }

    [Fact]
    public async Task With_the_store_down_the_device_is_told_to_try_later_and_no_record_is_written()
    {
        var s = await SeedAsync("down");
        Store.Unavailable = true;
        try
        {
            (await UploadAsync(s.TesterId, s.ClientId, Report)).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        }
        finally
        {
            Store.Unavailable = false;
        }
        (await RecordAsync(s.TestId)).Should().BeNull();
    }

    [Fact]
    public async Task A_lapsed_licence_can_still_send_the_report_for_work_already_done()
    {
        var s = await SeedAsync("lapsed");
        var client = _factory.CreateClientAs(Roles.Tester, s.TesterId, claims: $"{LicenceScope.ScopeClaim}={LicenceScope.SyncOnly}");

        (await client.PutAsync($"/api/sync/final-report/{s.ClientId}", Pdf(Report))).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Administrators_cannot_upload_through_the_tester_sync_surface()
    {
        var s = await SeedAsync("adminup");
        var admin = _factory.CreateClientAs(Roles.SuperAdministrator, "an-admin");

        (await admin.PutAsync($"/api/sync/final-report/{s.ClientId}", Pdf(Report))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---- Download ----------------------------------------------------------

    private sealed record ViewWithReport(Guid Id, FinalReportView? FinalReport);
    private sealed record FinalReportView(DateTimeOffset StoredAt, long SizeBytes);

    [Fact]
    public async Task A_super_administrator_downloads_the_report_as_signed_off()
    {
        var s = await SeedAsync("dl");
        await UploadAsync(s.TesterId, s.ClientId, Report);
        var admin = _factory.CreateClientAs(Roles.SuperAdministrator, "super-dl");

        var res = await admin.GetAsync($"/api/tests/{s.TestId}/final-report");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        res.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        (await res.Content.ReadAsByteArrayAsync()).Should().Equal(Report);
        res.Headers.CacheControl!.NoStore.Should().BeTrue("no PDF in any cache, the browser's included");
        res.Content.Headers.ContentDisposition!.FileNameStar.Should().Be("Test Summary - dl Farm - 2026-10-07 - as signed off.pdf",
            "named for the New Zealand date it was signed off (21:30 UTC on the 6th is the 7th in NZ)");
    }

    [Fact]
    public async Task The_test_view_says_whether_a_signed_off_copy_is_held()
    {
        var s = await SeedAsync("view");
        var admin = _factory.CreateClientAs(Roles.SuperAdministrator, "super-view");

        (await (await admin.GetAsync($"/api/tests/{s.TestId}")).Content.ReadFromJsonAsync<ViewWithReport>())!.FinalReport.Should().BeNull();
        await UploadAsync(s.TesterId, s.ClientId, Report);
        var view = await (await admin.GetAsync($"/api/tests/{s.TestId}")).Content.ReadFromJsonAsync<ViewWithReport>();

        view!.FinalReport!.SizeBytes.Should().Be(Report.Length);
    }

    [Fact]
    public async Task A_company_administrator_downloads_their_companys_but_not_anothers()
    {
        var mine = await SeedAsync("ca-mine");
        var theirs = await SeedAsync("ca-theirs");
        await UploadAsync(mine.TesterId, mine.ClientId, Report);
        await UploadAsync(theirs.TesterId, theirs.ClientId, Report);
        var adminId = $"ca-admin-{Guid.NewGuid():N}";
        await WithDbAsync(async db =>
        {
            db.Users.Add(new Tester { Id = adminId, UserName = adminId, DisplayName = "Company Admin", TestingCompanyId = mine.CompanyId });
            return await db.SaveChangesAsync();
        });
        var admin = _factory.CreateClientAs(Roles.CompanyAdministrator, adminId);

        (await admin.GetAsync($"/api/tests/{mine.TestId}/final-report")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.GetAsync($"/api/tests/{theirs.TestId}/final-report")).StatusCode
            .Should().Be(HttpStatusCode.NotFound, "another company's test doesn't exist as far as this admin can tell");
    }

    [Fact]
    public async Task A_tester_gets_a_colleagues_signed_off_report_but_never_another_companys()
    {
        var owner = await SeedAsync("tc-owner");
        await UploadAsync(owner.TesterId, owner.ClientId, Report);
        var colleague = await SeedAsync("tc-colleague", companyId: owner.CompanyId);
        var outsider = await SeedAsync("tc-outsider");

        (await _factory.CreateClientAs(Roles.Tester, colleague.TesterId).GetAsync($"/api/tests/{owner.TestId}/final-report"))
            .StatusCode.Should().Be(HttpStatusCode.OK, "the Company tests view already lets them regenerate it");
        (await _factory.CreateClientAs(Roles.Tester, outsider.TesterId).GetAsync($"/api/tests/{owner.TestId}/final-report"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task No_stored_copy_is_a_404_so_the_view_offers_only_the_regenerated_one()
    {
        var s = await SeedAsync("none");
        var admin = _factory.CreateClientAs(Roles.SuperAdministrator, "super-none");

        (await admin.GetAsync($"/api/tests/{s.TestId}/final-report")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task With_the_store_down_the_download_is_a_503_not_a_404()
    {
        var s = await SeedAsync("dl-down");
        await UploadAsync(s.TesterId, s.ClientId, Report);
        var admin = _factory.CreateClientAs(Roles.SuperAdministrator, "super-down");
        Store.Unavailable = true;
        try
        {
            (await admin.GetAsync($"/api/tests/{s.TestId}/final-report")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        }
        finally
        {
            Store.Unavailable = false;
        }
    }

    [Fact]
    public void The_download_is_named_as_the_device_names_its_own()
    {
        TestsController.FinalReportFileName("Pāuatahanui Farm (North)", new DateTimeOffset(2026, 10, 7, 1, 0, 0, TimeSpan.Zero))
            .Should().Be("Test Summary - Puatahanui Farm North - 2026-10-07 - as signed off.pdf");
    }

    /// <summary>A stream that won't say how long it is, so HttpClient sends it chunked.</summary>
    private sealed class UnknownLengthStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
    }
}
