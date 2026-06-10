using Autorep.Web.Data;
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
    public List<string> Errors { get; } = new();

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        var name = Name.Trim();
        var brand = string.IsNullOrWhiteSpace(Brand) ? null : Brand.Trim();
        if (!EquipmentItem.Types.Contains(Type)) Errors.Add("Unknown catalog type.");
        if (string.IsNullOrWhiteSpace(name)) Errors.Add("Name is required.");
        if (Type == EquipmentItem.Pulsator && brand is null) Errors.Add("Pulsator models need a brand.");
        if (Errors.Count == 0 && await _db.EquipmentItems.AnyAsync(x => x.Type == Type && x.Name == name && x.Brand == brand))
            Errors.Add("An item with that name (and brand) already exists in this catalog.");
        if (Errors.Count > 0) return Page();

        _db.EquipmentItems.Add(new EquipmentItem { Type = Type, Name = name, Brand = brand });
        await _db.SaveChangesAsync();
        return RedirectToPage("/Admin/Equipment/Index");
    }
}
