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

    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public int TesterCount { get; private set; }
    public List<string> Errors { get; } = new();
    public string? Message { get; set; }

    public class InputModel
    {
        public string Name { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var company = await _db.TestingCompanies.FindAsync(Id);
        if (company is null) return NotFound();
        Input.Name = company.Name;
        IsActive = company.IsActive;
        CreatedAt = company.CreatedAt;
        TesterCount = await _db.Users.CountAsync(u => u.TestingCompanyId == Id);
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
}
