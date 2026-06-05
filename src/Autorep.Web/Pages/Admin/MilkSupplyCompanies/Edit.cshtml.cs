using Autorep.Web.Data;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Pages.Admin.MilkSupplyCompanies;

public class EditModel : PageModel
{
    private readonly AutorepDbContext _db;
    public EditModel(AutorepDbContext db) => _db = db;

    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty] public InputModel Input { get; set; } = new();
    public bool IsActive { get; private set; }
    public int FarmCount { get; private set; }
    public List<string> Errors { get; } = new();
    public string? Message { get; set; }

    public class InputModel { public string Name { get; set; } = string.Empty; }

    public async Task<IActionResult> OnGetAsync()
    {
        var c = await _db.MilkSupplyCompanies.FindAsync(Id);
        if (c is null) return NotFound();
        Input.Name = c.Name;
        await PopulateAsync(c);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var c = await _db.MilkSupplyCompanies.FindAsync(Id);
        if (c is null) return NotFound();
        var trimmed = Input.Name.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            Errors.Add("Name is required.");
            await PopulateAsync(c);
            return Page();
        }
        if (await _db.MilkSupplyCompanies.AnyAsync(x => x.Name == trimmed && x.Id != Id))
        {
            Errors.Add($"Another processor named '{trimmed}' already exists.");
            await PopulateAsync(c);
            return Page();
        }
        c.Name = trimmed;
        await _db.SaveChangesAsync();
        Message = "Saved.";
        await PopulateAsync(c);
        return Page();
    }

    public async Task<IActionResult> OnPostToggleActiveAsync()
    {
        var c = await _db.MilkSupplyCompanies.FindAsync(Id);
        if (c is null) return NotFound();
        c.IsActive = !c.IsActive;
        await _db.SaveChangesAsync();
        return RedirectToPage(new { id = Id });
    }

    private async Task PopulateAsync(MilkSupplyCompany c)
    {
        IsActive = c.IsActive;
        FarmCount = await _db.Farms.CountAsync(f => f.MilkSupplyCompanyId == c.Id);
    }
}
