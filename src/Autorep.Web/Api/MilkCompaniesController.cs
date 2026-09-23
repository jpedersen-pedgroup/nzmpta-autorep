using Autorep.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Api;

// Serves milk-supply company logos to ANY authenticated user (Testers included) so logos
// can appear on tester screens / reports, not just the Super-Administrator admin pages.
[ApiController]
[Route("api/milk-companies")]
[Authorize]
public class MilkCompaniesController : ControllerBase
{
    private readonly AutorepDbContext _db;
    public MilkCompaniesController(AutorepDbContext db) => _db = db;

    [HttpGet("{id:guid}/logo")]
    public async Task<IActionResult> Logo(Guid id)
    {
        var c = await _db.MilkSupplyCompanies.FindAsync(id);
        if (c?.LogoData is null || c.LogoData.Length == 0) return NotFound();
        // An SVG can carry script. As an <img> it never runs, but opened directly at this URL it
        // would run on our origin; the sandbox policy stops that, and nosniff stops a browser from
        // second-guessing the type.
        Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'; style-src 'unsafe-inline'";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(c.LogoData, c.LogoContentType ?? "application/octet-stream");
    }
}
