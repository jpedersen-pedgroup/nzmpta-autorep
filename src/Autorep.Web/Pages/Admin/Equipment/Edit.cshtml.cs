using Autorep.Web.Data;
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
    public string Type { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public List<string> Errors { get; } = new();
    public string? Message { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var e = await _db.EquipmentItems.FindAsync(Id);
        if (e is null) return NotFound();
        Type = e.Type; Name = e.Name; Brand = e.Brand; IsActive = e.IsActive;
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
            Errors.Add("An item with that name (and brand) already exists in this catalog.");
        if (Errors.Count > 0) return Page();

        e.Name = name;
        e.Brand = brand;
        e.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();
        Message = "Saved. Devices receive the change on their next sync.";
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
}
