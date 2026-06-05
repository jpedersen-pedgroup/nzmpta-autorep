using Autorep.Web.Data;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Pages.Admin.Companies;

public class EditModel : PageModel
{
    private readonly AutorepDbContext _db;
    public EditModel(AutorepDbContext db) => _db = db;

    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty] public InputModel Input { get; set; } = new();
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public int TesterCount { get; private set; }
    public List<string> Errors { get; } = new();
    public string? Message { get; set; }

    public class InputModel
    {
        public string Name { get; set; } = string.Empty;
        public string? AddressLine1 { get; set; }
        public string? AddressLine2 { get; set; }
        public string? Town { get; set; }
        public string? PostCode { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var company = await _db.TestingCompanies.FindAsync(Id);
        if (company is null) return NotFound();
        Input.Name = company.Name;
        Input.AddressLine1 = company.AddressLine1;
        Input.AddressLine2 = company.AddressLine2;
        Input.Town = company.Town;
        Input.PostCode = company.PostCode;
        Input.Phone = company.Phone;
        Input.Email = company.Email;
        await PopulateAsync(company);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var company = await _db.TestingCompanies.FindAsync(Id);
        if (company is null) return NotFound();
        var trimmed = Input.Name.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            Errors.Add("Name is required.");
            await PopulateAsync(company);
            return Page();
        }
        company.Name = trimmed;
        company.AddressLine1 = Clean(Input.AddressLine1);
        company.AddressLine2 = Clean(Input.AddressLine2);
        company.Town = Clean(Input.Town);
        company.PostCode = Clean(Input.PostCode);
        company.Phone = Clean(Input.Phone);
        company.Email = Clean(Input.Email);
        await _db.SaveChangesAsync();
        Message = "Saved.";
        await PopulateAsync(company);
        return Page();
    }

    public async Task<IActionResult> OnPostToggleActiveAsync()
    {
        var company = await _db.TestingCompanies.FindAsync(Id);
        if (company is null) return NotFound();
        company.IsActive = !company.IsActive;
        await _db.SaveChangesAsync();
        return RedirectToPage(new { id = Id });
    }

    private async Task PopulateAsync(TestingCompany company)
    {
        IsActive = company.IsActive;
        CreatedAt = company.CreatedAt;
        TesterCount = await _db.Users.CountAsync(u => u.TestingCompanyId == company.Id);
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
