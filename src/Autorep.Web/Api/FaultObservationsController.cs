using Autorep.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Api;

// Read-side of the admin-managed fault-observation catalog (standard fault wording + CMM severity
// + default recommendation per visual check). Devices pull the active set + version stamp and
// cache it; the bundled defaults are the offline fallback. Editing happens in
// /Admin/FaultObservations (SuperAdmin).
[ApiController]
[Route("api/fault-observations")]
[Authorize]
public class FaultObservationsController : ControllerBase
{
    private readonly AutorepDbContext _db;
    public FaultObservationsController(AutorepDbContext db) => _db = db;

    public record FaultObservationDto(string Category, string Name, string Severity, string? Recommendation);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var rows = await _db.FaultObservations.ToListAsync(ct);
        var version = rows.Count > 0 ? rows.Max(f => f.UpdatedAt).ToString("o") : null;
        var items = rows
            .Where(f => f.IsActive)
            .OrderBy(f => f.Category).ThenBy(f => f.Name)
            .Select(f => new FaultObservationDto(f.Category, f.Name, f.Severity, f.Recommendation));
        return Ok(new { version, items });
    }
}
