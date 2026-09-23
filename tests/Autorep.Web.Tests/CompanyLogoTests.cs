using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Autorep.Web.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Autorep.Web.Tests;

// The testing company's report logo as managed on "My company": a Company Administrator changes
// only their own company's logo (a Super-Administrator any, from Companies/Edit), the upload follows
// LogoImage's rules, and the audit trail records the change without copying the image. The file
// rules themselves are covered by LogoImageTests; the device sync and report by the client tests.
public class CompanyLogoTests : IClassFixture<AuthedWebAppFactory>
{
    private readonly AuthedWebAppFactory _factory;
    public CompanyLogoTests(AuthedWebAppFactory factory) => _factory = factory;

    // A real 1×1 PNG, plus just-enough JPEG / GIF / SVG bytes for the signature checks.
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg==");
    private static readonly byte[] OtherPng = [.. Png, 0];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01];
    private static readonly byte[] Gif = "GIF89a\x01\x00\x01\x00"u8.ToArray();
    private static readonly byte[] Svg = "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray();

    // --- File rules (LogoImage) through the page ----------------------------------------------------

    [Fact]
    public async Task Company_administrator_can_upload_an_svg_logo()
    {
        var ownId = await SeedCompanyAsync("Vector Co", null);
        var adminId = "logo-admin-" + Guid.NewGuid().ToString("N");
        await SeedUserAsync(adminId, ownId);
        var client = NoCookieClientAs(Roles.CompanyAdministrator, adminId);

        var res = await PostMyCompanyAsync(client, Svg, smuggledCompanyId: null);

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LogoOfAsync(ownId)).Should().Equal(Svg);
        (await LogoTypeOfAsync(ownId)).Should().Be("image/svg+xml", "the stored type comes from the bytes, not the upload label");
    }

    [Fact]
    public async Task Company_administrator_upload_of_an_unprintable_format_is_rejected()
    {
        var ownId = await SeedCompanyAsync("Gif Co", Png);
        var adminId = "logo-admin-" + Guid.NewGuid().ToString("N");
        await SeedUserAsync(adminId, ownId);
        var client = NoCookieClientAs(Roles.CompanyAdministrator, adminId);

        var res = await PostMyCompanyAsync(client, Gif, smuggledCompanyId: null);

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.Content.ReadAsStringAsync()).Should().Contain(LogoImage.FormatError);
        (await LogoOfAsync(ownId)).Should().Equal(Png, "a rejected upload leaves the current logo alone");
    }

    // --- Who may change it -----------------------------------------------------------------------

    [Fact]
    public async Task Company_administrator_uploads_their_own_company_logo()
    {
        var ownId = await SeedCompanyAsync("Own Co", null);
        var adminId = "logo-admin-" + Guid.NewGuid().ToString("N");
        await SeedUserAsync(adminId, ownId);
        var client = NoCookieClientAs(Roles.CompanyAdministrator, adminId);
        (await client.GetStringAsync("/Admin")).Should().Contain("/Admin/MyCompany", "the admin home links to it");

        var res = await PostMyCompanyAsync(client, Png, smuggledCompanyId: null);

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.Content.ReadAsStringAsync()).Should().Contain("Logo saved");
        (await LogoOfAsync(ownId)).Should().Equal(Png);
    }

    // The core authorization guarantee: whatever id a Company Administrator puts in the form or the
    // route, only their own company's logo can change.
    [Fact]
    public async Task Company_administrator_cannot_change_another_companys_logo()
    {
        var ownId = await SeedCompanyAsync("Mine Co", null);
        var otherId = await SeedCompanyAsync("Theirs Co", Png);
        var adminId = "logo-admin-" + Guid.NewGuid().ToString("N");
        await SeedUserAsync(adminId, ownId);
        var client = NoCookieClientAs(Roles.CompanyAdministrator, adminId);

        // 1. My company, with the other company's id smuggled into the form and the query string.
        var res = await PostMyCompanyAsync(client, OtherPng, smuggledCompanyId: otherId);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LogoOfAsync(otherId)).Should().Equal(Png, "the other company's logo must be untouched");
        (await LogoOfAsync(ownId)).Should().Equal(OtherPng, "the upload lands on the admin's own company");

        // 2. The Super-Administrator company editor is closed to them, before anything is read.
        (await client.GetAsync($"/Admin/Companies/Edit/{otherId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var form = new MultipartFormDataContent
        {
            { new StringContent("Hijacked"), "Input.Name" },
            { new StringContent("true"), "Input.RemoveLogo" },
        };
        (await client.PostAsync($"/Admin/Companies/Edit/{otherId}", form)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await LogoOfAsync(otherId)).Should().Equal(Png);
    }

    [Fact]
    public async Task My_company_upload_requires_the_antiforgery_token()
    {
        var ownId = await SeedCompanyAsync("Csrf Co", Png);
        var adminId = "logo-admin-" + Guid.NewGuid().ToString("N");
        await SeedUserAsync(adminId, ownId);
        var client = NoCookieClientAs(Roles.CompanyAdministrator, adminId);

        using var form = new MultipartFormDataContent { { new StringContent("true"), "Input.RemoveLogo" } };
        var res = await client.PostAsync("/Admin/MyCompany", form);

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LogoOfAsync(ownId)).Should().Equal(Png);
    }

    [Fact]
    public async Task Company_administrator_removes_their_logo()
    {
        var ownId = await SeedCompanyAsync("Removing Co", Png);
        var adminId = "logo-admin-" + Guid.NewGuid().ToString("N");
        await SeedUserAsync(adminId, ownId);
        var client = NoCookieClientAs(Roles.CompanyAdministrator, adminId);

        var (token, cookie) = await AntiforgeryAsync(client, "/Admin/MyCompany");
        using var form = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" },
            { new StringContent("true"), "Input.RemoveLogo" },
        };
        var req = new HttpRequestMessage(HttpMethod.Post, "/Admin/MyCompany") { Content = form };
        req.Headers.Add("Cookie", cookie);
        var res = await client.SendAsync(req);

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LogoOfAsync(ownId)).Should().BeNull();
    }

    [Fact]
    public async Task Super_administrator_can_set_any_companys_logo_and_is_sent_there_from_my_company()
    {
        var companyId = await SeedCompanyAsync("Any Co", null);
        var client = NoCookieClientAs(Roles.SuperAdministrator, "super-logo");

        var redirect = await client.GetAsync("/Admin/MyCompany");
        redirect.StatusCode.Should().Be(HttpStatusCode.Redirect);
        redirect.Headers.Location!.ToString().Should().Contain("/Admin/Companies");

        var (token, cookie) = await AntiforgeryAsync(client, $"/Admin/Companies/Edit/{companyId}");
        using var form = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" },
            { new StringContent("Any Co"), "Input.Name" },
            { FileContent(Jpeg, "logo.jpg"), "Input.Logo", "logo.jpg" },
        };
        var req = new HttpRequestMessage(HttpMethod.Post, $"/Admin/Companies/Edit/{companyId}") { Content = form };
        req.Headers.Add("Cookie", cookie);
        var res = await client.SendAsync(req);

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LogoOfAsync(companyId)).Should().Equal(Jpeg);
    }

    // --- Audit -----------------------------------------------------------------------------------

    [Fact]
    public async Task Audit_entry_records_a_logo_change_as_a_hash_not_a_copy_of_the_image()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
        var company = new TestingCompany { Name = "Audited Co " + Guid.NewGuid(), LogoData = Png, LogoContentType = "image/png" };
        db.TestingCompanies.Add(company);
        await db.SaveChangesAsync();
        company.LogoData = OtherPng;
        await db.SaveChangesAsync();

        var entry = await db.AuditEntries
            .Where(e => e.EntityType == nameof(TestingCompany) && e.EntityKey == company.Id.ToString() && e.Operation == "Modified")
            .SingleAsync();
        entry.BeforeJson.Should().Contain("[binary: len=").And.NotContain(Convert.ToBase64String(Png));
        entry.AfterJson.Should().Contain("[binary: len=").And.NotContain(Convert.ToBase64String(OtherPng));
        entry.BeforeJson.Should().NotBe(entry.AfterJson, "the hash still shows that the logo changed");
    }

    // --- Helpers ---------------------------------------------------------------------------------

    private async Task<Guid> SeedCompanyAsync(string name, byte[]? logo)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
        var company = new TestingCompany
        {
            Name = name + " " + Guid.NewGuid().ToString("N")[..6],
            LogoData = logo,
            LogoContentType = logo is null ? null : "image/png",
        };
        db.TestingCompanies.Add(company);
        await db.SaveChangesAsync();
        return company.Id;
    }

    private async Task SeedUserAsync(string userId, Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
        db.Users.Add(new Tester { Id = userId, UserName = userId, Email = userId + "@test.local", TestingCompanyId = companyId });
        await db.SaveChangesAsync();
    }

    private async Task<string?> LogoTypeOfAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
        return (await db.TestingCompanies.AsNoTracking().SingleAsync(c => c.Id == companyId)).LogoContentType;
    }

    private async Task<byte[]?> LogoOfAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
        return (await db.TestingCompanies.AsNoTracking().SingleAsync(c => c.Id == companyId)).LogoData;
    }

    // Cookies are handled by hand (the antiforgery cookie is read off the GET and sent with the
    // POST) so the test controls exactly what reaches the server.
    private HttpClient NoCookieClientAs(string role, string userId)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, role);
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId);
        return client;
    }

    private static async Task<(string Token, string Cookie)> AntiforgeryAsync(HttpClient client, string path)
    {
        var page = await client.GetAsync(path);
        page.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await page.Content.ReadAsStringAsync();
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        token.Should().NotBeNullOrEmpty();
        var cookie = page.Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith(".AspNetCore.Antiforgery", StringComparison.Ordinal))
            .Split(';')[0];
        return (token, cookie);
    }

    private static ByteArrayContent FileContent(byte[] bytes, string fileName)
    {
        var content = new ByteArrayContent(bytes);
        // Deliberately a misleading type: the server must go by the bytes.
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return content;
    }

    private async Task<HttpResponseMessage> PostMyCompanyAsync(HttpClient client, byte[] logo, Guid? smuggledCompanyId)
    {
        var (token, cookie) = await AntiforgeryAsync(client, "/Admin/MyCompany");
        var form = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" },
            { FileContent(logo, "logo.png"), "Input.Logo", "logo.png" },
        };
        var path = "/Admin/MyCompany";
        if (smuggledCompanyId is { } smuggled)
        {
            form.Add(new StringContent(smuggled.ToString()), "Id");
            form.Add(new StringContent(smuggled.ToString()), "CompanyId");
            form.Add(new StringContent(smuggled.ToString()), "Company.Id");
            path += $"?id={smuggled}&companyId={smuggled}";
        }
        var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = form };
        req.Headers.Add("Cookie", cookie);
        return await client.SendAsync(req);
    }
}
