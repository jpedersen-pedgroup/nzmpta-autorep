using Autorep.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Api;

// Read-side of the admin-managed test standards: Devices pull the full set (it's small) plus a
// version stamp (the latest UpdatedAt) so the client can tell the Tester when standards changed
// since they last synced. Editing happens in /Admin/Standards (SuperAdmin).
[ApiController]
[Route("api/standards")]
[Authorize]
public class StandardsController : ControllerBase
{
    private readonly AutorepDbContext _db;
    public StandardsController(AutorepDbContext db) => _db = db;

    public record StandardDto(
        string Key, string Label, string Category, string Kind,
        double? Limit, double? Min, double? Max, double? Target, double? Tolerance, double? Value,
        string? Unit, string? SourceRef);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var rows = await _db.TestStandards
            .OrderBy(s => s.Category).ThenBy(s => s.Key)
            .ToListAsync(ct);

        var version = rows.Count > 0 ? rows.Max(s => s.UpdatedAt).ToString("o") : null;
        var standards = rows.Select(s => new StandardDto(
            s.Key, s.Label, s.Category, s.Kind,
            s.Limit, s.Min, s.Max, s.Target, s.Tolerance, s.Value,
            s.Unit, s.SourceRef));

        return Ok(new { version, standards });
    }
}
