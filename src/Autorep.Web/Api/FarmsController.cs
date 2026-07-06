using System.Security.Claims;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Api;

// Read-only farm lookup for the Tester wizard, which snapshots the chosen farm's details
// for offline display. Farm editing stays in the Admin portal. (Bulk reference-data sync of
// farms into IndexedDB — for fully-offline farm pick — is a later M2 step.)
[ApiController]
[Route("api/farms")]
[Authorize]
public class FarmsController : ControllerBase
{
    private readonly AutorepDbContext _db;

    public FarmsController(AutorepDbContext db) => _db = db;

    public record FarmDto(
        Guid Id, string Name, string? SupplyNumber, string? AddressLine1, string? AddressLine2,
        string? Town, string? PostCode, string? RapidNumber, string? RegionName, string? MilkCompanyName,
        string? FarmerName, string? ContactPhone, string? ContactEmail, Guid? MilkCompanyId = null);

    // List: the caller's farm book, cached on-device at app load so the wizard can resolve a
    // farm chosen while offline (including farms the office added since the last visit).
    // Same visibility rule as the New-test picker — the shared FarmScope predicate — never the
    // whole national list (farm PII scoping).
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var query = _db.Farms
            .Include(x => x.Region)
            .Include(x => x.MilkSupplyCompany)
            .Where(x => x.IsActive);

        if (!User.IsInRole(Roles.SuperAdministrator))
        {
            var testerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var companyId = await _db.Users
                .Where(u => u.Id == testerId)
                .Select(u => u.TestingCompanyId)
                .FirstOrDefaultAsync(ct);

            query = query.InCompanyScope(_db, companyId, testerId);
        }

        var farms = await query
            .OrderBy(x => x.Name)
            .Select(f => new FarmDto(
                f.Id, f.Name, f.SupplyNumber, f.AddressLine1, f.AddressLine2,
                f.Town, f.PostCode, f.RapidNumber,
                f.Region == null ? null : f.Region.Name,
                f.MilkSupplyCompany == null ? null : f.MilkSupplyCompany.Name,
                f.FarmerName, f.ContactPhone, f.ContactEmail, f.MilkSupplyCompanyId))
            .ToListAsync(ct);

        return Ok(farms);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        // Scope to farms the caller has a legitimate relationship with, so a tester can't harvest
        // every farmer's contact details by iterating ids. Super-Admins see any farm; everyone
        // else only farms in their company's scope (set up or tested by the company — the shared
        // FarmScope predicate). Return NotFound for out-of-scope ids so their existence isn't
        // disclosed.
        var query = _db.Farms
            .Include(x => x.Region)
            .Include(x => x.MilkSupplyCompany)
            .Where(x => x.Id == id);

        if (!User.IsInRole(Roles.SuperAdministrator))
        {
            var testerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var companyId = await _db.Users
                .Where(u => u.Id == testerId)
                .Select(u => u.TestingCompanyId)
                .FirstOrDefaultAsync(ct);

            query = query.InCompanyScope(_db, companyId, testerId);
        }

        var f = await query.FirstOrDefaultAsync(ct);

        if (f is null) return NotFound();

        return Ok(new FarmDto(
            f.Id, f.Name, f.SupplyNumber, f.AddressLine1, f.AddressLine2,
            f.Town, f.PostCode, f.RapidNumber, f.Region?.Name, f.MilkSupplyCompany?.Name,
            f.FarmerName, f.ContactPhone, f.ContactEmail, f.MilkSupplyCompanyId));
    }
}
