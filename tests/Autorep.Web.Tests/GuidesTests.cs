using System.Net;
using System.Net.Http.Headers;
using Autorep.Web.Domain;
using Autorep.Web.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Autorep.Web.Tests;

// Help & guides: the work-instruction PDFs are served by GuidesController behind sign-in, and each
// guide's roles (Guides/guides.json) decide who may download it — the Help page only hides links,
// so this is the check that actually keeps the admin guides from a Tester.
public class GuidesTests : IClassFixture<AuthedWebAppFactory>
{
    private const string TesterGuide = "/guides/autorep-tester-guide.pdf";
    private const string CompanyAdminGuide = "/guides/autorep-company-admin-guide.pdf";
    private const string SuperAdminGuide = "/guides/autorep-super-admin-guide.pdf";

    private readonly AuthedWebAppFactory _factory;
    public GuidesTests(AuthedWebAppFactory factory) => _factory = factory;

    [Fact]
    public void Every_catalogued_guide_has_its_pdf()
    {
        // GuideCatalog hides (and logs) an entry whose file is missing; a publish that forgot the PDF
        // should fail here rather than quietly drop a guide from everyone's Help page.
        var catalog = _factory.Services.GetRequiredService<GuideCatalog>();

        catalog.All.Select(g => g.Id).Should().BeEquivalentTo(["tester", "company-admin", "super-admin"]);
        catalog.All.Should().OnlyContain(g => g.SizeBytes > 0 && g.Hash.Length == 12);
    }

    [Fact]
    public async Task Service_worker_guide_list_matches_the_catalogue()
    {
        // sw.js prunes its offline guide copies against GUIDE_FILES (written in by tools/stamp-sw.mjs).
        // A catalogue edit shipped without `npm run build:prod` would leave a retired guide on devices,
        // or prune a current one.
        var catalog = _factory.Services.GetRequiredService<GuideCatalog>();
        var sw = await _factory.CreateClient().GetStringAsync("/sw.js");

        var line = System.Text.RegularExpressions.Regex.Match(sw, @"const GUIDE_FILES = \[([^\]]*)\];").Groups[1].Value;
        var files = System.Text.RegularExpressions.Regex.Matches(line, "'([^']+)'").Select(m => m.Groups[1].Value);

        files.Should().BeEquivalentTo(catalog.All.Select(g => g.File));
    }

    [Fact]
    public async Task Tester_can_fetch_the_tester_guide()
    {
        var client = _factory.CreateClientAs(Roles.Tester);

        var resp = await client.GetAsync(TesterGuide);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        bytes.Take(5).Should().Equal("%PDF-"u8.ToArray());
        // Opened in the device's viewer, not forced to a download.
        resp.Content.Headers.ContentDisposition!.DispositionType.Should().Be("inline");
    }

    [Theory]
    [InlineData(CompanyAdminGuide)]
    [InlineData(SuperAdminGuide)]
    public async Task Tester_cannot_fetch_an_admin_guide(string url)
    {
        var client = _factory.CreateClientAs(Roles.Tester);

        var resp = await client.GetAsync(url);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(TesterGuide)]
    [InlineData(CompanyAdminGuide)]
    public async Task Company_admin_can_fetch_their_guides(string url)
    {
        var client = _factory.CreateClientAs(Roles.CompanyAdministrator);

        var resp = await client.GetAsync(url);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Company_admin_cannot_fetch_the_super_admin_guide()
    {
        var client = _factory.CreateClientAs(Roles.CompanyAdministrator);

        var resp = await client.GetAsync(SuperAdminGuide);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(TesterGuide)]
    [InlineData(CompanyAdminGuide)]
    [InlineData(SuperAdminGuide)]
    public async Task Super_admin_can_fetch_every_guide(string url)
    {
        var client = _factory.CreateClientAs(Roles.SuperAdministrator);

        var resp = await client.GetAsync(url);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(TesterGuide)]
    [InlineData(SuperAdminGuide)]
    public async Task Anonymous_cannot_fetch_any_guide(string url)
    {
        // No role header = anonymous behind the test scheme, which challenges with 401 (the real
        // cookie scheme redirects to sign-in instead).
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var resp = await client.GetAsync(url);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("/guides/not-a-guide.pdf")]
    [InlineData("/guides/..%2Fguides.json")]
    [InlineData("/guides/guides.json")]
    public async Task Only_catalogued_files_are_served(string url)
    {
        var client = _factory.CreateClientAs(Roles.SuperAdministrator);

        var resp = await client.GetAsync(url);

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_unchanged_guide_revalidates_as_304()
    {
        // The service worker revalidates its offline copy with the ETag on every open while online;
        // this is what keeps that cheap — and a new PDF (new hash) what replaces the copy.
        var client = _factory.CreateClientAs(Roles.Tester);
        var first = await client.GetAsync(TesterGuide);
        var etag = first.Headers.ETag;
        etag.Should().NotBeNull();

        var again = new HttpRequestMessage(HttpMethod.Get, TesterGuide);
        again.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag!.Tag));
        var resp = await client.SendAsync(again);

        resp.StatusCode.Should().Be(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task Help_page_lists_only_the_testers_guide_for_a_tester()
    {
        var client = _factory.CreateClientAs(Roles.Tester);

        var html = await client.GetStringAsync("/Help");

        html.Should().Contain("Tester work instructions")
            .And.NotContain("Company Administrator work instructions")
            .And.NotContain("Super Administrator work instructions");
        // Versioned link, so no browser cache holds a replaced guide.
        html.Should().MatchRegex(@"/guides/autorep-tester-guide\.pdf\?v=[0-9a-f]{12}");
    }

    [Fact]
    public async Task Help_page_lists_the_company_admins_two_guides()
    {
        var client = _factory.CreateClientAs(Roles.CompanyAdministrator);

        var html = await client.GetStringAsync("/Help");

        html.Should().Contain("Tester work instructions")
            .And.Contain("Company Administrator work instructions")
            .And.NotContain("Super Administrator work instructions");
    }

    [Fact]
    public async Task Help_page_lists_all_three_for_a_super_admin()
    {
        var client = _factory.CreateClientAs(Roles.SuperAdministrator);

        var html = await client.GetStringAsync("/Help");

        html.Should().Contain("Tester work instructions")
            .And.Contain("Company Administrator work instructions")
            .And.Contain("Super Administrator work instructions");
    }

    [Fact]
    public async Task Help_page_requires_sign_in()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var resp = await client.GetAsync("/Help");

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
