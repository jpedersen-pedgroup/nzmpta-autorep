using Autorep.Web.Data;
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

    public async Task<IActionResult> OnGetAsync()
    {
        var e = await _db.EquipmentItems.FindAsync(Id);
        if (e is null) return NotFound();
        Type = e.Type; Name = e.Name; Brand = e.Brand; IsActive = e.IsActive;
        Spec = SpecInput.From(e);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var e = await _db.EquipmentItems.FindAsync(Id);
        if (e is null) return NotFound();
        Type = e.Type; IsActive = e.IsActive;

        var name = Name.Trim();
        var brand = string.IsNullOrWhiteSpace(Brand) ? null : Brand.Trim();
        if (string.IsNullOrWhiteSpace(name)) Errors.Add("Name is required.");
        if (await _db.EquipmentItems.AnyAsync(x => x.Id != Id && x.Type == e.Type && x.Name == name && x.Brand == brand))
            Errors.Add("An item with that name (and brand) already exists for this equipment type.");
        if (OemSpecs.HasSpec(e.Type))
        {
            if (SpecFieldsUnreadable()) Errors.Add("Enter the spec figures as numbers.");
            Spec.Validate(e.Type, Errors);
        }
        if (Errors.Count > 0) return Page();

        // A rename keeps the spec: it belongs to the item, and devices find it by the item's id.
        e.Name = name;
        e.Brand = brand;
        if (OemSpecs.HasSpec(e.Type)) e.SpecJson = Spec.ToSpecJson(e.Type);
        e.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();
        Message = OemSpecs.HasSpec(e.Type)
            ? "Saved. Devices receive the change on their next sync; tests already signed off keep the spec they were judged against."
            : "Saved. Devices receive the change on their next sync.";
        return Page();
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

    // A figure the binder couldn't read as a number arrives as null, which would quietly clear it.
    private bool SpecFieldsUnreadable() =>
        ModelState.Any(kv => kv.Key.StartsWith(nameof(Spec) + ".", StringComparison.Ordinal) && kv.Value is { Errors.Count: > 0 });
}
