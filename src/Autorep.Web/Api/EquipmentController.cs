using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Api;

// Read-side of the admin-managed OEM Specs (the equipment catalogs: shells / liners / pulsator models
// / pumps / milkline sizes / pulsator configurations / regulator types, and the pulsator and vacuum
// pump specs). Devices pull the set + a version stamp and cache it in IndexedDB; the bundled defaults
// are the offline fallback. Editing happens in /Admin/Equipment (SuperAdmin).
//
// `items` is the ACTIVE set, which builds the dropdowns. `inactive` carries the deactivated items
// too, for their specs only: a test already recording a model keeps its spec when the model is
// deactivated. A device that predates specs reads `items` and ignores the rest.
[ApiController]
[Route("api/equipment")]
[Authorize]
public class EquipmentController : ControllerBase
{
    private readonly AutorepDbContext _db;
    public EquipmentController(AutorepDbContext db) => _db = db;

    /// <summary>One item. <see cref="Specs"/> is its OEM spec history, oldest first: a test is judged
    /// against the version in effect on the day it was started, and devices hold versions dated ahead
    /// so they switch on the day, offline or not. <see cref="Spec"/> is the version in effect today,
    /// for devices from before effective dates. Both are always sent, so a device can tell "no spec"
    /// from a server that doesn't send specs.</summary>
    public record EquipmentDto(Guid Id, string Type, string Name, string? Brand, object? Spec, IReadOnlyList<SpecVersionDto> Specs);

    /// <summary>A spec version. <see cref="EffectiveFrom"/> is an NZ date (yyyy-mm-dd), or null for the
    /// version in effect from the start; <see cref="Spec"/> is null when the model has no spec from then.</summary>
    public record SpecVersionDto(string? EffectiveFrom, object? Spec);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var rows = await _db.EquipmentItems.Include(e => e.SpecVersions).ToListAsync(ct);
        // Version covers all rows (deactivations and spec edits must bump it too).
        var version = rows.Count > 0 ? rows.Max(e => e.UpdatedAt).ToString("o") : null;
        var today = NzTime.Today;
        var ordered = rows.OrderBy(e => e.Type).ThenBy(e => e.Brand).ThenBy(e => e.Name).ToList();
        var items = ordered.Where(e => e.IsActive).Select(e => ToDto(e, today));
        var inactive = ordered.Where(e => !e.IsActive && OemSpecs.HasSpec(e.Type)).Select(e => ToDto(e, today));
        return Ok(new { version, items, inactive });
    }

    private static EquipmentDto ToDto(EquipmentItem e, DateOnly today) => new(
        e.Id, e.Type, e.Name, e.Brand,
        OemSpecs.Parse(e.Type, OemSpecs.InEffect(e.SpecVersions, today)?.SpecJson),
        OemSpecs.InOrder(e.SpecVersions)
            .Select(v => new SpecVersionDto(v.EffectiveFrom?.ToString("yyyy-MM-dd"), OemSpecs.Parse(e.Type, v.SpecJson)))
            .ToList());
}
