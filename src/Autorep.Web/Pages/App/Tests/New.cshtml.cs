using System.Security.Claims;
using Autorep.Web.Data;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Pages.App.Tests;

public class NewModel : PageModel
{
    private readonly AutorepDbContext _db;
    public NewModel(AutorepDbContext db) => _db = db;

    [BindProperty] public InputModel Input { get; set; } = new();
    public List<string> Errors { get; } = new();

    public record FarmRow(Guid Id, string Name, Guid? MilkCompanyId);
    public List<FarmRow> Farms { get; private set; } = new();
    public List<SelectListItem> RegionOptions { get; private set; } = new();
    public List<SelectListItem> MilkCompanyOptions { get; private set; } = new();
    public string CollectionNotice { get; private set; } = "";

    public class InputModel
    {
        public Guid? FarmId { get; set; }
        public string? Notes { get; set; }
        public bool MarkComplete { get; set; }
    }

    // Payload from the "add a new farm" modal.
    public class NewFarmModel
    {
        public string Name { get; set; } = string.Empty;
        public string? SupplyNumber { get; set; }
        public Guid? MilkSupplyCompanyId { get; set; }
        public Guid? RegionId { get; set; }
        public string? AddressLine1 { get; set; }
        public string? AddressLine2 { get; set; }
        public string? Town { get; set; }
        public string? PostCode { get; set; }
        public string? RapidNumber { get; set; }
        public string? FarmerName { get; set; }
        public string? ContactPhone { get; set; }
        public string? ContactEmail { get; set; }
    }

    public async Task OnGetAsync() => await LoadListsAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadListsAsync();
        // Only active farms in this company's scope appear in the picker; re-check both here so
        // a stale page or crafted POST can't start a test against a deactivated farm or another
        // company's farm (the picker's scoping must hold server-side, not just in the list).
        var farm = Input.FarmId is null
            ? null
            : await _db.Farms
                .Where(f => f.Id == Input.FarmId && f.IsActive)
                .InCompanyScope(_db, await CompanyIdAsync(), TesterId())
                .FirstOrDefaultAsync();
        if (farm is null)
        {
            Errors.Add("Select an existing farm, or add a new one.");
            return Page();
        }

        // Offline-first: the Machine Test is created on-device in the wizard (IndexedDB) and
        // synced to the server later. Here we just hand the chosen farm to the wizard.
        return RedirectToPage("/App/Tests/Wizard", new { farmId = farm.Id, farmName = farm.Name });
    }

    // AJAX from the modal: create a farm and return it for the picker (no navigation).
    public async Task<IActionResult> OnPostCreateFarmAsync([FromBody] NewFarmModel farm)
    {
        var name = farm.Name?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { error = "Farm name is required." });

        // A tester without a Testing Company can't own the new farm, so it would fall outside
        // every scope check (including this page's own OnPost) the moment it was created —
        // refuse up front rather than strand an orphan farm row.
        var companyId = await CompanyIdAsync();
        if (companyId is null)
            return BadRequest(new { error = "Your account isn't linked to a Testing Company, so it can't add farms. Ask your administrator." });

        var entity = new Farm
        {
            Name = name,
            // Tag the creating company so it appears in this company's farm picker straight away,
            // even before the first test is synced against it.
            CreatedByTestingCompanyId = companyId,
            SupplyNumber = Clean(farm.SupplyNumber),
            MilkSupplyCompanyId = farm.MilkSupplyCompanyId,
            RegionId = farm.RegionId,
            AddressLine1 = Clean(farm.AddressLine1),
            AddressLine2 = Clean(farm.AddressLine2),
            Town = Clean(farm.Town),
            PostCode = Clean(farm.PostCode),
            RapidNumber = Clean(farm.RapidNumber),
            FarmerName = Clean(farm.FarmerName),
            ContactPhone = Clean(farm.ContactPhone),
            ContactEmail = Clean(farm.ContactEmail),
        };
        _db.Farms.Add(entity);
        await _db.SaveChangesAsync();
        return new JsonResult(new { id = entity.Id, name = entity.Name, milkCompanyId = entity.MilkSupplyCompanyId });
    }

    private string? TesterId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

    // The Testing Company the signed-in tester belongs to (scopes the farm picker).
    private async Task<Guid?> CompanyIdAsync()
    {
        var testerId = TesterId();
        return await _db.Users.Where(u => u.Id == testerId)
            .Select(u => u.TestingCompanyId).FirstOrDefaultAsync();
    }

    private async Task LoadListsAsync()
    {
        // Only show farms in this tester's company scope (set up or tested by the company —
        // the shared FarmScope predicate) — not the whole national list.
        Farms = await _db.Farms
            .Where(f => f.IsActive)
            .InCompanyScope(_db, await CompanyIdAsync(), TesterId())
            .OrderBy(f => f.Name)
            .Select(f => new FarmRow(f.Id, f.Name, f.MilkSupplyCompanyId))
            .ToListAsync();

        var regions = await _db.Regions.Where(r => r.IsActive)
            .OrderBy(r => r.Island).ThenBy(r => r.SortOrder).ThenBy(r => r.Name).ToListAsync();
        var groups = new Dictionary<string, SelectListGroup>();
        RegionOptions = regions.Select(r =>
        {
            var island = string.IsNullOrWhiteSpace(r.Island) ? "Other" : r.Island;
            if (!groups.TryGetValue(island, out var g)) { g = new SelectListGroup { Name = island }; groups[island] = g; }
            return new SelectListItem { Value = r.Id.ToString(), Text = r.Name, Group = g };
        }).ToList();

        MilkCompanyOptions = await _db.MilkSupplyCompanies.Where(c => c.IsActive).OrderBy(c => c.Name)
            .Select(c => new SelectListItem { Value = c.Id.ToString(), Text = c.Name })
            .ToListAsync();

        CollectionNotice = await _db.PrivacyContent.OrderByDescending(p => p.UpdatedAt)
            .Select(p => p.CollectionNotice).FirstOrDefaultAsync() ?? "";
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
