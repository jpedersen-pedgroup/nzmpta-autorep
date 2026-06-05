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
    public bool HasLogo { get; private set; }
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
        public IFormFile? Logo { get; set; }
        public bool RemoveLogo { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var c = await _db.MilkSupplyCompanies.FindAsync(Id);
        if (c is null) return NotFound();
        Input.Name = c.Name;
        Input.AddressLine1 = c.AddressLine1;
        Input.AddressLine2 = c.AddressLine2;
        Input.Town = c.Town;
        Input.PostCode = c.PostCode;
        Input.Phone = c.Phone;
        Input.Email = c.Email;
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

        if (Input.RemoveLogo)
        {
            c.LogoData = null;
            c.LogoContentType = null;
        }
        else if (!await LogoUpload.ApplyAsync(Input.Logo, c, Errors))
        {
            await PopulateAsync(c);
            return Page();
        }

        c.Name = trimmed;
        c.AddressLine1 = Clean(Input.AddressLine1);
        c.AddressLine2 = Clean(Input.AddressLine2);
        c.Town = Clean(Input.Town);
        c.PostCode = Clean(Input.PostCode);
        c.Phone = Clean(Input.Phone);
        c.Email = Clean(Input.Email);
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
        HasLogo = c.LogoData != null && c.LogoData.Length > 0;
        FarmCount = await _db.Farms.CountAsync(f => f.MilkSupplyCompanyId == c.Id);
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
