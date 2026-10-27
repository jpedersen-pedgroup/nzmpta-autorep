using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Pages.Admin.Equipment;

public class EditModel : PageModel
{
    private readonly AutorepDbContext _db;
    public EditModel(AutorepDbContext db) => _db = db;

    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty] public string Name { get; set; } = string.Empty;
    [BindProperty] public string? Brand { get; set; }
    [BindProperty] public SpecInput Spec { get; set; } = new();
    public string Type { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public List<string> Errors { get; } = new();
    public string? Message { get; set; }

    /// <summary>One version of the spec, for the history table.</summary>
    public record VersionRow(Guid Id, DateOnly? EffectiveFrom, string? Summary, bool InEffect, bool Scheduled);
    public IList<VersionRow> History { get; private set; } = [];
    public DateOnly Today { get; private set; }

    /// <summary>`from` (yyyy-mm-dd, today or later) loads the version for that date into the form, to
    /// change one already scheduled; by default the form holds today's spec, to apply from today.</summary>
    public async Task<IActionResult> OnGetAsync(DateOnly? from)
    {
        var e = await LoadAsync();
        if (e is null) return NotFound();
        Name = e.Name; Brand = e.Brand;
        var date = from is { } f && f >= Today ? f : Today;
        Spec = SpecInput.From(e.Type, OemSpecs.InEffect(e.SpecVersions, date)?.SpecJson, date);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var e = await LoadAsync();
        if (e is null) return NotFound();

        var name = Name.Trim();
        var brand = string.IsNullOrWhiteSpace(Brand) ? null : Brand.Trim();
        if (string.IsNullOrWhiteSpace(name)) Errors.Add("Name is required.");
        if (await _db.EquipmentItems.AnyAsync(x => x.Id != Id && x.Type == e.Type && x.Name == name && x.Brand == brand))
            Errors.Add("An item with that name (and brand) already exists for this equipment type.");
        var hasSpec = OemSpecs.HasSpec(e.Type);
        if (hasSpec)
        {
            if (SpecFieldsUnreadable()) Errors.Add("Enter the spec figures as numbers.");
            Spec.Validate(e.Type, Errors);
            Spec.ValidateEffectiveFrom(Today, Errors);
        }
        if (Errors.Count > 0) return Page();

        // A rename keeps the spec: it belongs to the item, and devices find it by the item's id.
        var changed = e.Name != name || e.Brand != brand;
        e.Name = name;
        e.Brand = brand;
        var specChanged = hasSpec && SaveVersion(e, Spec.EffectiveFrom!.Value, Spec.ToSpecJson(e.Type));
        if (changed || specChanged)
        {
            e.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync();
        }
        Message = !specChanged
            ? "Saved. Devices receive the change on their next sync."
            : Spec.EffectiveFrom == Today
                ? "Saved. Tests started from today are judged against the new spec; tests started earlier keep the one they began with."
                : $"Saved. Tests started from {Spec.EffectiveFrom:d MMM yyyy} are judged against the new spec. Devices receive it on their next sync and switch over on the day.";
        History = HistoryOf(e);
        return Page();
    }

    /// <summary>Removes a version that isn't in effect yet; history already in effect stays.</summary>
    public async Task<IActionResult> OnPostDeleteVersionAsync(Guid versionId)
    {
        var e = await LoadAsync();
        if (e is null) return NotFound();
        var version = e.SpecVersions.FirstOrDefault(v => v.Id == versionId);
        if (version is null || version.EffectiveFrom is not { } from || from <= Today)
        {
            Errors.Add("Only a spec that hasn't come into effect yet can be removed.");
            Name = e.Name; Brand = e.Brand;
            Spec = SpecInput.From(e.Type, OemSpecs.InEffect(e.SpecVersions, Today)?.SpecJson, Today);
            return Page();
        }
        _db.EquipmentSpecVersions.Remove(version);
        e.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostToggleActiveAsync()
    {
        var e = await _db.EquipmentItems.FindAsync(Id);
        if (e is null) return NotFound();
        e.IsActive = !e.IsActive;
        e.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var e = await _db.EquipmentItems.FindAsync(Id);
        if (e is null) return NotFound();
        _db.EquipmentItems.Remove(e);
        await _db.SaveChangesAsync();
        return RedirectToPage("/Admin/Equipment/Index");
    }

    /// <summary>Writes the spec to apply from <paramref name="from"/>: replaces the version dated that
    /// day, or adds one when the figures differ from those that would otherwise apply then. True when
    /// anything changed.</summary>
    private bool SaveVersion(EquipmentItem e, DateOnly from, string? specJson)
    {
        var sameDay = e.SpecVersions.FirstOrDefault(v => v.EffectiveFrom == from);
        if (sameDay is not null)
        {
            if (sameDay.SpecJson == specJson) return false;
            sameDay.SpecJson = specJson;
            sameDay.CreatedAt = DateTimeOffset.UtcNow;
            return true;
        }
        if (OemSpecs.InEffect(e.SpecVersions, from)?.SpecJson == specJson) return false;
        // Added through the DbSet, which also puts it in e.SpecVersions.
        _db.EquipmentSpecVersions.Add(new EquipmentSpecVersion { EquipmentItemId = e.Id, EffectiveFrom = from, SpecJson = specJson });
        return true;
    }

    private async Task<EquipmentItem?> LoadAsync()
    {
        Today = NzTime.Today;
        var e = await _db.EquipmentItems.Include(x => x.SpecVersions).FirstOrDefaultAsync(x => x.Id == Id);
        if (e is null) return null;
        Type = e.Type; IsActive = e.IsActive;
        History = HistoryOf(e);
        return e;
    }

    private IList<VersionRow> HistoryOf(EquipmentItem e)
    {
        var current = OemSpecs.InEffect(e.SpecVersions, Today);
        return OemSpecs.InOrder(e.SpecVersions)
            .Select(v => new VersionRow(v.Id, v.EffectiveFrom, SpecInput.Summary(e.Type, v.SpecJson, detailed: true),
                v == current, v.EffectiveFrom > Today))
            .ToList();
    }

    // A figure the binder couldn't read as a number arrives as null, which would quietly clear it.
    private bool SpecFieldsUnreadable() =>
        ModelState.Any(kv => kv.Key.StartsWith(nameof(Spec) + ".", StringComparison.Ordinal) && kv.Value is { Errors.Count: > 0 });
}
