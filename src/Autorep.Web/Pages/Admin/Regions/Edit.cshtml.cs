using Autorep.Web.Data;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Pages.Admin.Regions;

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

    public class InputModel
    {
        public string Name { get; set; } = string.Empty;
        public string Island { get; set; } = "North Island";
        public int SortOrder { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var r = await _db.Regions.FindAsync(Id);
        if (r is null) return NotFound();
        Input.Name = r.Name;
        Input.Island = string.IsNullOrEmpty(r.Island) ? "North Island" : r.Island;
        Input.SortOrder = r.SortOrder;
        await PopulateAsync(r);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var r = await _db.Regions.FindAsync(Id);
        if (r is null) return NotFound();
        var trimmed = Input.Name.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            Errors.Add("Name is required.");
            await PopulateAsync(r);
            return Page();
        }
        if (await _db.Regions.AnyAsync(x => x.Name == trimmed && x.Id != Id))
        {
            Errors.Add($"Another region named '{trimmed}' already exists.");
            await PopulateAsync(r);
            return Page();
        }
        r.Name = trimmed;
        r.Island = Input.Island;
        r.SortOrder = Input.SortOrder;
        await _db.SaveChangesAsync();
        Message = "Saved.";
        await PopulateAsync(r);
        return Page();
    }

    public async Task<IActionResult> OnPostToggleActiveAsync()
    {
        var r = await _db.Regions.FindAsync(Id);
        if (r is null) return NotFound();
        r.IsActive = !r.IsActive;
        await _db.SaveChangesAsync();
        return RedirectToPage(new { id = Id });
    }

    private async Task PopulateAsync(Region r)
    {
        IsActive = r.IsActive;
        FarmCount = await _db.Farms.CountAsync(f => f.RegionId == r.Id);
    }
}
