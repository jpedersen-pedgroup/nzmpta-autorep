using Autorep.Web.Data;
using Autorep.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace Autorep.Web.Api;

// Serves testing-company logos to ANY authenticated user (mirrors MilkCompaniesController): the
// Tester's device downloads its company's logo on sync so the Test Summary can print it offline,
// and the admin pages preview it. A logo is branding, not company data, so it isn't scoped.
[ApiController]
[Route("api/testing-companies")]
[Authorize]
public class TestingCompaniesController : ControllerBase
{
    private readonly AutorepDbContext _db;
    public TestingCompaniesController(AutorepDbContext db) => _db = db;

    [HttpGet("{id:guid}/logo")]
    public async Task<IActionResult> Logo(Guid id, CancellationToken ct)
    {
        var logo = await _db.TestingCompanies
            .Where(c => c.Id == id)
            .Select(c => new { c.LogoData, c.LogoContentType })
            .FirstOrDefaultAsync(ct);
        if (logo?.LogoData is null || logo.LogoData.Length == 0) return NotFound();

        // Always revalidate, answered by the content-hash ETag: an unchanged logo costs the device a
        // 304 on each sync, and a replaced one arrives on the very next sync. (FileContentResult
        // handles If-None-Match itself.)
        Response.Headers.CacheControl = "private, no-cache";
        // Legacy-migrated logos predate the PNG/JPEG rule and may be anything — never let the
        // browser run one as a document if it's opened directly.
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; sandbox";

        var contentType = CompanyLogo.SniffContentType(logo.LogoData)
            ?? logo.LogoContentType
            ?? "application/octet-stream";
        return File(logo.LogoData, contentType, lastModified: null,
            entityTag: new EntityTagHeaderValue(CompanyLogo.ETag(logo.LogoData)));
    }
}
