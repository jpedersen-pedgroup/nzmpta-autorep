using Autorep.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Api;

// Read-side of the admin-managed equipment catalogs (shells / liners / pulsator models /
// milkline sizes / pulsator configurations). Devices pull the ACTIVE set + a version stamp and
// cache it in IndexedDB; the bundled defaults are the offline fallback. Editing happens in
// /Admin/Equipment (SuperAdmin).
[ApiController]
[Route("api/equipment")]
[Authorize]
public class EquipmentController : ControllerBase
{
    private readonly AutorepDbContext _db;
    public EquipmentController(AutorepDbContext db) => _db = db;

    public record EquipmentDto(string Type, string Name, string? Brand);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var rows = await _db.EquipmentItems.ToListAsync(ct);
        // Version covers all rows (deactivations must bump it too).
        var version = rows.Count > 0 ? rows.Max(e => e.UpdatedAt).ToString("o") : null;
        var items = rows
            .Where(e => e.IsActive)
            .OrderBy(e => e.Type).ThenBy(e => e.Brand).ThenBy(e => e.Name)
            .Select(e => new EquipmentDto(e.Type, e.Name, e.Brand));
        return Ok(new { version, items });
    }
}
