using System.Net;
using System.Security.Claims;
using System.Text;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Autorep.Web.Pages.Admin.Companies;
using Autorep.Web.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Autorep.Web.Tests;

// Logos print on the on-device report, whose generator can draw only PNG, JPEG and SVG, so an
// upload is judged on its bytes and stored under the type it really is.
public class LogoImageTests : IClassFixture<AuthedWebAppFactory>
{
    private readonly AuthedWebAppFactory _factory;
    public LogoImageTests(AuthedWebAppFactory factory) => _factory = factory;

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 16];
    private static readonly byte[] Gif = Encoding.ASCII.GetBytes("GIF89a\u0001\u0000");
    private static readonly byte[] Webp = Encoding.ASCII.GetBytes("RIFF\u0000\u0000\u0000\u0000WEBPVP8 ");
    private static byte[] Text(string s) => Encoding.UTF8.GetBytes(s);

    // ---- Sniff -----------------------------------------------------------------------------

    [Fact]
    public void Recognises_png_jpeg_and_svg_by_their_bytes()
    {
        LogoImage.Sniff(Png).Should().Be("image/png");
        LogoImage.Sniff(Jpeg).Should().Be("image/jpeg");
        LogoImage.Sniff(Text("<svg xmlns=\"http://www.w3.org/2000/svg\"/>")).Should().Be("image/svg+xml");
    }

    [Theory]
    [InlineData("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<svg viewBox=\"0 0 10 10\"></svg>")]
    [InlineData("<?xml version=\"1.0\"?><!-- Generator: Adobe Illustrator --><!DOCTYPE svg PUBLIC \"-//W3C//DTD SVG 1.1//EN\" \"x.dtd\">\r\n<svg>\n</svg>")]
    [InlineData("\n\n  <svg\n  width=\"10\"/>")]
    public void Recognises_svg_after_a_prolog_comments_and_doctype(string svg)
    {
        LogoImage.Sniff(Text(svg)).Should().Be("image/svg+xml");
        LogoImage.Sniff([0xEF, 0xBB, 0xBF, .. Text(svg)]).Should().Be("image/svg+xml", "a UTF-8 BOM is allowed");
    }

    [Theory]
    [InlineData("<html><body><svg></svg></body></html>")] // svg inside something else
    [InlineData("<svgfoo/>")]
    [InlineData("just some text")]
    [InlineData("")]
    public void Rejects_markup_whose_root_is_not_svg(string text) =>
        LogoImage.Sniff(Text(text)).Should().BeNull();

    [Fact]
    public void Rejects_gif_webp_and_truncated_signatures()
    {
        LogoImage.Sniff(Gif).Should().BeNull();
        LogoImage.Sniff(Webp).Should().BeNull();
        LogoImage.Sniff(Png[..4]).Should().BeNull();
        LogoImage.Sniff(Jpeg[..2]).Should().BeNull();
    }

    // ---- Upload ----------------------------------------------------------------------------

    private static IFormFile File(byte[] data, string name, string contentType) =>
        new FormFile(new MemoryStream(data), 0, data.Length, "Input.Logo", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };

    [Fact]
    public async Task No_file_is_fine_and_changes_nothing()
    {
        var errors = new List<string>();
        var (ok, logo) = await LogoImage.ReadUploadAsync(null, errors);
        ok.Should().BeTrue();
        logo.Should().BeNull();
        errors.Should().BeEmpty();
    }

    [Fact]
    public async Task A_gif_renamed_to_png_is_rejected()
    {
        var errors = new List<string>();
        var (ok, logo) = await LogoImage.ReadUploadAsync(File(Gif, "logo.png", "image/png"), errors);
        ok.Should().BeFalse();
        logo.Should().BeNull();
        errors.Should().ContainSingle().Which.Should().Be(LogoImage.FormatError);
    }

    [Fact]
    public async Task A_mislabelled_png_is_stored_as_what_it_really_is()
    {
        var (ok, logo) = await LogoImage.ReadUploadAsync(File(Png, "logo.gif", "image/gif"), []);
        ok.Should().BeTrue();
        logo!.ContentType.Should().Be("image/png");
        logo.Data.Should().Equal(Png);
    }

    [Fact]
    public async Task A_logo_over_1_MB_is_rejected()
    {
        var big = new byte[LogoImage.MaxBytes + 1];
        Png.CopyTo(big, 0);
        var errors = new List<string>();
        (await LogoImage.ReadUploadAsync(File(big, "big.png", "image/png"), errors)).Ok.Should().BeFalse();
        errors.Should().ContainSingle().Which.Should().Contain("1 MB");
    }

    // ---- Testing company edit page ---------------------------------------------------------

    private static AutorepDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AutorepDbContext>().UseInMemoryDatabase("logo-" + Guid.NewGuid()).Options);

    private static EditModel EditPage(AutorepDbContext db, Guid id, EditModel.InputModel input) => new(db)
    {
        Id = id,
        Input = input,
        PageContext = new PageContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, Roles.SuperAdministrator)], "Test")),
            },
        },
    };

    [Fact]
    public async Task Testing_company_edit_stores_an_uploaded_logo()
    {
        using var db = NewDb();
        var company = new TestingCompany { Name = "Upload Co" };
        db.TestingCompanies.Add(company);
        await db.SaveChangesAsync();

        var page = EditPage(db, company.Id, new EditModel.InputModel { Name = "Upload Co", Logo = File(Jpeg, "logo.jpg", "image/jpeg") });
        (await page.OnPostAsync()).Should().BeOfType<PageResult>();

        page.Errors.Should().BeEmpty();
        var saved = await db.TestingCompanies.SingleAsync(c => c.Id == company.Id);
        saved.LogoData.Should().Equal(Jpeg);
        saved.LogoContentType.Should().Be("image/jpeg");
        page.LogoPreview.Should().StartWith("data:image/jpeg;base64,");
    }

    [Fact]
    public async Task Testing_company_edit_rejects_an_unsupported_logo_and_saves_nothing()
    {
        using var db = NewDb();
        var company = new TestingCompany { Name = "Keep Co", LogoData = Png, LogoContentType = "image/png" };
        db.TestingCompanies.Add(company);
        await db.SaveChangesAsync();

        var page = EditPage(db, company.Id, new EditModel.InputModel { Name = "Renamed Co", Logo = File(Webp, "logo.webp", "image/webp") });
        await page.OnPostAsync();

        page.Errors.Should().Contain(LogoImage.FormatError);
        var saved = await db.TestingCompanies.SingleAsync(c => c.Id == company.Id);
        saved.Name.Should().Be("Keep Co", "a rejected upload fails the whole save");
        saved.LogoData.Should().Equal(Png);
    }

    [Fact]
    public async Task Testing_company_edit_can_remove_the_logo()
    {
        using var db = NewDb();
        var company = new TestingCompany { Name = "Remove Co", LogoData = Png, LogoContentType = "image/png" };
        db.TestingCompanies.Add(company);
        await db.SaveChangesAsync();

        await EditPage(db, company.Id, new EditModel.InputModel { Name = "Remove Co", RemoveLogo = true }).OnPostAsync();

        var saved = await db.TestingCompanies.SingleAsync(c => c.Id == company.Id);
        saved.LogoData.Should().BeNull();
        saved.LogoContentType.Should().BeNull();
    }

    // ---- Serving ---------------------------------------------------------------------------

    [Fact]
    public async Task A_served_logo_is_sandboxed_so_script_in_an_svg_cannot_run()
    {
        Guid id;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
            var milk = new MilkSupplyCompany
            {
                Name = "Svg Milk " + Guid.NewGuid(),
                LogoData = Text("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"),
                LogoContentType = "image/svg+xml",
            };
            db.MilkSupplyCompanies.Add(milk);
            await db.SaveChangesAsync();
            id = milk.Id;
        }

        var res = await _factory.CreateClientAs(Roles.Tester, "svg-viewer").GetAsync($"/api/milk-companies/{id}/logo");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        res.Headers.GetValues("Content-Security-Policy").Single().Should().StartWith("sandbox");
        res.Headers.GetValues("X-Content-Type-Options").Single().Should().Be("nosniff");
    }
}
