using Autorep.Web.Data;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Pages.Admin.Regions;

public class NewModel : PageModel
{
    private readonly AutorepDbContext _db;
    public NewModel(AutorepDbContext db) => _db = db;

    [BindProperty] public InputModel Input { get; set; } = new();
    public List<string> Errors { get; } = new();

    public class InputModel
    {
        public string Name { get; set; } = string.Empty;
        public string Island { get; set; } = "North Island";
        public int SortOrder { get; set; }
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        var trimmed = Input.Name.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            Errors.Add("Name is required.");
            return Page();
        }
        if (await _db.Regions.AnyAsync(r => r.Name == trimmed))
        {
            Errors.Add($"A region named '{trimmed}' already exists.");
            return Page();
        }
        _db.Regions.Add(new Region { Name = trimmed, Island = Input.Island, SortOrder = Input.SortOrder });
        await _db.SaveChangesAsync();
        return RedirectToPage("/Admin/Regions/Index");
    }
}
