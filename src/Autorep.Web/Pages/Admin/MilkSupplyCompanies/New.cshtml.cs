using Autorep.Web.Data;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Pages.Admin.MilkSupplyCompanies;

public class NewModel : PageModel
{
    private readonly AutorepDbContext _db;
    public NewModel(AutorepDbContext db) => _db = db;

    [BindProperty] public InputModel Input { get; set; } = new();
    public List<string> Errors { get; } = new();

    public class InputModel { public string Name { get; set; } = string.Empty; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        var trimmed = Input.Name.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            Errors.Add("Name is required.");
            return Page();
        }
        if (await _db.MilkSupplyCompanies.AnyAsync(c => c.Name == trimmed))
        {
            Errors.Add($"A processor named '{trimmed}' already exists.");
            return Page();
        }
        _db.MilkSupplyCompanies.Add(new MilkSupplyCompany { Name = trimmed });
        await _db.SaveChangesAsync();
        return RedirectToPage("/Admin/MilkSupplyCompanies/Index");
    }
}
