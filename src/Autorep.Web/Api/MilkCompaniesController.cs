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
        return File(c.LogoData, c.LogoContentType ?? "application/octet-stream");
    }
}
