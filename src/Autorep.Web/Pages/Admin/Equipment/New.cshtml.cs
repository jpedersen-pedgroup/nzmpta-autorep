using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Pages.Admin.Equipment;

public class NewModel : PageModel
{
    private readonly AutorepDbContext _db;
    public NewModel(AutorepDbContext db) => _db = db;

    [BindProperty] public string Type { get; set; } = EquipmentItem.Shell;
    [BindProperty] public string Name { get; set; } = string.Empty;
    [BindProperty] public string? Brand { get; set; }
    [BindProperty] public SpecInput Spec { get; set; } = new();
    public List<string> Errors { get; } = new();

    public void OnGet() => Spec.EffectiveFrom = NzTime.Today;

    public async Task<IActionResult> OnPostAsync()
    {
        var name = Name.Trim();
        var brand = string.IsNullOrWhiteSpace(Brand) ? null : Brand.Trim();
        if (!EquipmentItem.Types.Contains(Type)) Errors.Add("Unknown equipment type.");
        if (string.IsNullOrWhiteSpace(name)) Errors.Add("Name is required.");
        if (EquipmentItem.HasBrand(Type) && brand is null)
            Errors.Add(Type == EquipmentItem.Pulsator ? "Pulsator models need a brand." : "Pumps need a make.");
        if (OemSpecs.HasSpec(Type))
        {
            if (ModelState.Any(kv => kv.Key.StartsWith(nameof(Spec) + ".", StringComparison.Ordinal) && kv.Value is { Errors.Count: > 0 }))
                Errors.Add("Enter the spec figures as numbers.");
            Spec.Validate(Type, Errors);
            Spec.ValidateEffectiveFrom(NzTime.Today, Errors);
        }
        if (Errors.Count == 0 && await _db.EquipmentItems.AnyAsync(x => x.Type == Type && x.Name == name && x.Brand == brand))
            Errors.Add("An item with that name (and brand) already exists for this equipment type.");
        if (Errors.Count > 0) return Page();

        var item = new EquipmentItem { Type = Type, Name = name, Brand = brand };
        if (OemSpecs.HasSpec(Type) && Spec.ToSpecJson(Type) is { } spec)
            item.SpecVersions.Add(new EquipmentSpecVersion { EffectiveFrom = Spec.EffectiveFrom, SpecJson = spec });
        _db.EquipmentItems.Add(item);
        await _db.SaveChangesAsync();
        return RedirectToPage("/Admin/Equipment/Index");
    }
}
