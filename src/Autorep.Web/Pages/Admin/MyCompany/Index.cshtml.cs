using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Autorep.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Pages.Admin.MyCompany;

// "My company" — the Company Administrator's own Testing Company: its details (read-only; NZMPTA
// maintains them) and the report logo, which the Company Administrator manages themselves.
//
// There is deliberately no company id in the route or the form: the company is always the one on
// the signed-in principal, so it can't be widened by editing a URL or a hidden field (the same rule
// as the Company tests API). A Super-Administrator manages every company's logo from
// /Admin/Companies/Edit instead, and is sent there.
public class IndexModel : PageModel
{
    private readonly AutorepDbContext _db;
    private readonly UserManager<Tester> _users;
    public IndexModel(AutorepDbContext db, UserManager<Tester> users)
    {
        _db = db;
        _users = users;
    }

    [BindProperty] public InputModel Input { get; set; } = new();
    /// <summary>Null when the signed-in administrator isn't attached to a Testing Company.</summary>
    public TestingCompany? Company { get; private set; }
    public int TesterCount { get; private set; }
    public bool HasLogo { get; private set; }
    public bool LogoPrintable { get; private set; }
    public string? LogoVersion { get; private set; }
    public List<string> Errors { get; } = new();
    public string? Message { get; set; }

    public class InputModel
    {
        public IFormFile? Logo { get; set; }
        public bool RemoveLogo { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (User.IsInRole(Roles.SuperAdministrator)) return RedirectToPage("/Admin/Companies/Index");
        Company = await OwnCompanyAsync();
        if (Company is not null) await PopulateAsync(Company);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (User.IsInRole(Roles.SuperAdministrator)) return RedirectToPage("/Admin/Companies/Index");
        Company = await OwnCompanyAsync();
        if (Company is null) return Forbid();

        if (Input.RemoveLogo)
        {
            Company.LogoData = null;
            Company.LogoContentType = null;
        }
        else if (Input.Logo is null || Input.Logo.Length == 0)
        {
            Errors.Add("Choose a PNG or JPEG file to upload.");
            await PopulateAsync(Company);
            return Page();
        }
        else if (!await CompanyLogo.ApplyAsync(Input.Logo, Company, Errors))
        {
            await PopulateAsync(Company);
            return Page();
        }

        // Audited by the AuditInterceptor like any other company change (the logo bytes are
        // recorded as a length + hash, not copied).
        await _db.SaveChangesAsync();
        Message = Input.RemoveLogo
            ? "Logo removed. Reports will print without a company logo."
            : "Logo saved. Testers' devices will pick it up the next time they sync.";
        await PopulateAsync(Company);
        return Page();
    }

    private async Task<TestingCompany?> OwnCompanyAsync()
    {
        var me = await _users.GetUserAsync(User);
        if (me?.TestingCompanyId is not { } companyId) return null;
        return await _db.TestingCompanies.FindAsync(companyId);
    }

    private async Task PopulateAsync(TestingCompany company)
    {
        HasLogo = company.LogoData is { Length: > 0 };
        LogoPrintable = CompanyLogo.IsPrintable(company.LogoData);
        LogoVersion = HasLogo ? CompanyLogo.ETag(company.LogoData!).Trim('"') : null;
        TesterCount = await _db.Users.CountAsync(u => u.TestingCompanyId == company.Id);
    }
}
