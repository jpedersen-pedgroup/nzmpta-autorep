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

    public class InputModel
    {
        public string Name { get; set; } = string.Empty;
        public string? AddressLine1 { get; set; }
        public string? AddressLine2 { get; set; }
        public string? Town { get; set; }
        public string? PostCode { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public IFormFile? Logo { get; set; }
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
        if (await _db.MilkSupplyCompanies.AnyAsync(c => c.Name == trimmed))
        {
            Errors.Add($"A processor named '{trimmed}' already exists.");
            return Page();
        }

        var company = new MilkSupplyCompany
        {
            Name = trimmed,
            AddressLine1 = Clean(Input.AddressLine1),
            AddressLine2 = Clean(Input.AddressLine2),
            Town = Clean(Input.Town),
            PostCode = Clean(Input.PostCode),
            Phone = Clean(Input.Phone),
            Email = Clean(Input.Email),
        };
        if (!await LogoUpload.ApplyAsync(Input.Logo, company, Errors)) return Page();

        _db.MilkSupplyCompanies.Add(company);
        await _db.SaveChangesAsync();
        return RedirectToPage("/Admin/MilkSupplyCompanies/Index");
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
