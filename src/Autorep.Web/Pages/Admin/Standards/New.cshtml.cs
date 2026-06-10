using Autorep.Web.Data;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Pages.Admin.Standards;

public class NewModel : PageModel
{
    private readonly AutorepDbContext _db;
    public NewModel(AutorepDbContext db) => _db = db;

    [BindProperty] public string Key { get; set; } = string.Empty;
    [BindProperty] public StandardInput Input { get; set; } = new();
    public List<string> Errors { get; } = new();

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        var key = Key.Trim();
        if (string.IsNullOrWhiteSpace(key)) Errors.Add("Key is required (the reading key or a param.* name).");
        else if (await _db.TestStandards.AnyAsync(s => s.Key == key)) Errors.Add($"A standard with key '{key}' already exists.");
        if (!Input.Validate(Errors)) return Page();

        var s = new TestStandard { Key = key };
        Input.Apply(s);
        _db.TestStandards.Add(s);
        await _db.SaveChangesAsync();
        return RedirectToPage("/Admin/Standards/Index");
    }
}
