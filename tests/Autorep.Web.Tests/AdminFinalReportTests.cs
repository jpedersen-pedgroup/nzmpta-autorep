using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Autorep.Web.Services.Pdfs;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Autorep.Web.Tests.TestPayloads;

namespace Autorep.Web.Tests;

// A version an administrator saved keeps the report the admin viewer made for it right after the save
// (PUT /api/admin/tests/{id}/final-report), as a tester's device keeps the one it signed off: stored
// the same way, served from the same route. Only for versions made in the admin portal — a tester's
// version keeps its device's copy — and within the administrator's scope.
public class AdminFinalReportTests : IClassFixture<AuthedWebAppFactory>
{
    private readonly AuthedWebAppFactory _factory;
    public AdminFinalReportTests(AuthedWebAppFactory factory) => _factory = factory;

    private static readonly byte[] Report = "%PDF-1.7\n% version 2 as saved\n%%EOF"u8.ToArray();

    private IServiceProvider Services => _factory.Services;
    private InMemoryPdfStore Store => Services.GetRequiredService<InMemoryPdfStore>();

    private async Task<T> WithDbAsync<T>(Func<AutorepDbContext, Task<T>> f)
    {
        using var scope = Services.CreateScope();
        return await f(scope.ServiceProvider.GetRequiredService<AutorepDbContext>());
    }

    private sealed record Arranged(MachineTest V1, Guid V2Id, Guid V2ClientId, string TesterId, HttpClient Admin);

    /// <summary>A tester's completed test and a Super-Administrator's version 2 of it.</summary>
    private async Task<Arranged> ArrangeAsync(string tag)
    {
        var (companyId, farmId) = await SeedCompanyAsync(Services, tag);
        var testerId = $"{tag}-tester";
        await SeedUserAsync(Services, testerId, companyId);
        await SeedUserAsync(Services, $"{tag}-coadmin", companyId);
        await SeedUserAsync(Services, $"{tag}-admin", null, "Sam Superadmin");
        var v1Id = Guid.NewGuid();
        var payload = Original(v1Id);
        var v1 = await SeedTestAsync(Services, testerId, farmId, companyId, payload, clientId: v1Id);
        var admin = _factory.CreateClientAs(Roles.SuperAdministrator, $"{tag}-admin");
        var res = await admin.PostAsJsonAsync($"/api/admin/tests/{v1.Id}/versions", new
        {
            payloadJson = Edited(payload, p => p["notes"] = "Corrected").ToJsonString(),
            reason = "Comment corrected",
        });
        res.StatusCode.Should().Be(HttpStatusCode.Created, await res.Content.ReadAsStringAsync());
        var saved = (await res.Content.ReadFromJsonAsync<JsonObject>())!;
        return new Arranged(v1, saved["id"]!.GetValue<Guid>(), saved["clientId"]!.GetValue<Guid>(), testerId, admin);
    }

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, Guid id, byte[] bytes, string contentType = "application/pdf")
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return client.PutAsync($"/api/admin/tests/{id}/final-report", content);
    }

    [Fact]
    public async Task An_admin_versions_report_is_kept_under_the_tester_and_served_like_a_signed_off_one()
    {
        var a = await ArrangeAsync("afr-keep");

        var res = await PutAsync(a.Admin, a.V2Id, Report);

        res.StatusCode.Should().Be(HttpStatusCode.Created, await res.Content.ReadAsStringAsync());
        (await Store.GetAsync(PdfContainer.FinalReports, PdfKeys.FinalReport(a.TesterId, a.V2ClientId)))!.Bytes.Should().Equal(Report);
        var record = await WithDbAsync(db => db.FinalReportBlobs.AsNoTracking().SingleAsync(r => r.MachineTestId == a.V2Id));
        record.StoredBy.Should().Be("afr-keep-admin");
        record.SizeBytes.Should().Be(Report.Length);

        var download = await a.Admin.GetAsync($"/api/tests/{a.V2Id}/final-report");
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync()).Should().Equal(Report);
        var view = (await a.Admin.GetFromJsonAsync<JsonObject>($"/api/tests/{a.V2Id}"))!;
        view["finalReport"]!["sizeBytes"]!.GetValue<long>().Should().Be(Report.Length);

        // The same report again (a retry): nothing changes.
        var again = await PutAsync(a.Admin, a.V2Id, Report);
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        (await again.Content.ReadFromJsonAsync<JsonObject>())!["status"]!.GetValue<string>().Should().Be("unchanged");
    }

    [Fact]
    public async Task A_testers_version_keeps_the_report_its_device_signed_off()
    {
        var a = await ArrangeAsync("afr-tester");

        var res = await PutAsync(a.Admin, a.V1.Id, Report);

        res.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await res.Content.ReadFromJsonAsync<JsonObject>())!["error"]!.GetValue<string>().Should().Be("not-an-admin-version");
        (await WithDbAsync(db => db.FinalReportBlobs.AnyAsync(r => r.MachineTestId == a.V1.Id))).Should().BeFalse();
    }

    [Fact]
    public async Task Is_scoped_like_the_save()
    {
        var a = await ArrangeAsync("afr-scope");
        var (other, _) = await SeedCompanyAsync(Services, "afr-scope-other");
        await SeedUserAsync(Services, "afr-scope-otheradmin", other);

        (await PutAsync(_factory.CreateClientAs(Roles.CompanyAdministrator, "afr-scope-otheradmin"), a.V2Id, Report))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "another company's test isn't disclosed");
        (await PutAsync(_factory.CreateClientAs(Roles.Tester, a.TesterId), a.V2Id, Report))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await PutAsync(_factory.CreateClientAs(Roles.CompanyAdministrator, "afr-scope-coadmin"), a.V2Id, Report))
            .StatusCode.Should().Be(HttpStatusCode.Created, "the test's own company administrator may");
    }

    [Fact]
    public async Task Only_a_pdf_is_kept()
    {
        var a = await ArrangeAsync("afr-pdf");

        (await PutAsync(a.Admin, a.V2Id, Report, "application/octet-stream")).StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        (await PutAsync(a.Admin, a.V2Id, "not a pdf"u8.ToArray())).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await WithDbAsync(db => db.FinalReportBlobs.AnyAsync(r => r.MachineTestId == a.V2Id))).Should().BeFalse();
    }

    [Fact]
    public async Task The_store_being_down_is_a_503_and_keeps_nothing()
    {
        var a = await ArrangeAsync("afr-down");
        Store.Unavailable = true;
        try
        {
            (await PutAsync(a.Admin, a.V2Id, Report)).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        }
        finally
        {
            Store.Unavailable = false;
        }
        (await WithDbAsync(db => db.FinalReportBlobs.AnyAsync(r => r.MachineTestId == a.V2Id))).Should().BeFalse();
    }
}
