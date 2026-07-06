using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Pages.Admin.Farms;

public class EditModel : PageModel
{
    private readonly AutorepDbContext _db;
    private readonly UserManager<Tester> _users;
    public EditModel(AutorepDbContext db, UserManager<Tester> users)
    {
        _db = db;
        _users = users;
    }

    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty] public InputModel Input { get; set; } = new();

    public string FarmName { get; private set; } = "";
    public int TestCount { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public List<SelectListItem> RegionOptions { get; private set; } = new();
    public SelectList MilkCompanyOptions { get; private set; } = default!;
    public List<string> Errors { get; } = new();
    public string? Message { get; set; }
    public string CollectionNotice { get; private set; } = "";

    public class InputModel
    {
        public string Name { get; set; } = string.Empty;
        public string? SupplyNumber { get; set; }
        public Guid? MilkSupplyCompanyId { get; set; }
        public string? AddressLine1 { get; set; }
        public string? AddressLine2 { get; set; }
        public string? Town { get; set; }
        public Guid? RegionId { get; set; }
        public string? PostCode { get; set; }
        public string? RapidNumber { get; set; }
        public string? FarmerName { get; set; }
        public string? ContactPhone { get; set; }
        public string? ContactEmail { get; set; }
        public string? Notes { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var farm = await _db.Farms.FindAsync(Id);
        if (farm is null) return NotFound();
        if (!await CanEditAsync(farm.Id)) return Forbid();

        Input = new InputModel
        {
            Name = farm.Name,
            SupplyNumber = farm.SupplyNumber,
            MilkSupplyCompanyId = farm.MilkSupplyCompanyId,
            AddressLine1 = farm.AddressLine1,
            AddressLine2 = farm.AddressLine2,
            Town = farm.Town,
            RegionId = farm.RegionId,
            PostCode = farm.PostCode,
            RapidNumber = farm.RapidNumber,
            FarmerName = farm.FarmerName,
            ContactPhone = farm.ContactPhone,
            ContactEmail = farm.ContactEmail,
            Notes = farm.Notes,
            IsActive = farm.IsActive,
        };
        await PopulateAsync(farm);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var farm = await _db.Farms.FindAsync(Id);
        if (farm is null) return NotFound();
        if (!await CanEditAsync(farm.Id)) return Forbid();

        var name = Input.Name?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name))
            Errors.Add("Farm name is required.");
        if (!string.IsNullOrWhiteSpace(Input.ContactEmail) && !Input.ContactEmail.Contains('@'))
            Errors.Add("Contact email doesn't look valid.");

        if (Errors.Count > 0)
        {
            await PopulateAsync(farm);
            return Page();
        }

        farm.Name = name;
        farm.SupplyNumber = Clean(Input.SupplyNumber);
        farm.MilkSupplyCompanyId = Input.MilkSupplyCompanyId;
        farm.AddressLine1 = Clean(Input.AddressLine1);
        farm.AddressLine2 = Clean(Input.AddressLine2);
        farm.Town = Clean(Input.Town);
        farm.RegionId = Input.RegionId;
        farm.PostCode = Clean(Input.PostCode);
        farm.RapidNumber = Clean(Input.RapidNumber);
        farm.FarmerName = Clean(Input.FarmerName);
        farm.ContactPhone = Clean(Input.ContactPhone);
        farm.ContactEmail = Clean(Input.ContactEmail);
        farm.Notes = Clean(Input.Notes);
        farm.IsActive = Input.IsActive;
        farm.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();

        Message = "Farm details saved.";
        await PopulateAsync(farm);
        return Page();
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // Super-Administrator may edit any farm. A Company Administrator may edit a farm only if
    // it is in their company's scope — set up or tested by the company (the shared FarmScope
    // predicate, so the edit guard always agrees with the Farms list).
    private async Task<bool> CanEditAsync(Guid farmId)
    {
        if (User.IsInRole(Roles.SuperAdministrator)) return true;
        var me = await _users.GetUserAsync(User);
        return await _db.Farms
            .Where(f => f.Id == farmId)
            .InCompanyScope(_db, me?.TestingCompanyId, me?.Id)
            .AnyAsync();
    }

    private async Task PopulateAsync(Farm farm)
    {
        FarmName = farm.Name;
        UpdatedAt = farm.UpdatedAt;
        // Count the tests the viewer can actually reach: a Company Administrator's "View tests"
        // deep link is pinned to their own company, so the count must match it.
        if (User.IsInRole(Roles.SuperAdministrator))
        {
            TestCount = await _db.MachineTests.CountAsync(t => t.FarmId == farm.Id);
        }
        else
        {
            var companyId = (await _users.GetUserAsync(User))?.TestingCompanyId;
            TestCount = await _db.MachineTests.CountAsync(t => t.FarmId == farm.Id
                && _db.Users.Any(u => u.Id == t.TesterId && u.TestingCompanyId == companyId));
        }
        CollectionNotice = await _db.PrivacyContent.OrderByDescending(p => p.UpdatedAt)
            .Select(p => p.CollectionNotice).FirstOrDefaultAsync() ?? "";
        // Include the farm's current region even if it's since been deactivated, so an
        // unrelated edit doesn't silently drop the existing association.
        var regions = await _db.Regions.Where(r => r.IsActive || r.Id == Input.RegionId)
            .OrderBy(r => r.Island).ThenBy(r => r.SortOrder).ThenBy(r => r.Name).ToListAsync();
        var islandGroups = new Dictionary<string, SelectListGroup>();
        RegionOptions = regions.Select(r =>
        {
            var island = string.IsNullOrWhiteSpace(r.Island) ? "Other" : r.Island;
            if (!islandGroups.TryGetValue(island, out var group))
            {
                group = new SelectListGroup { Name = island };
                islandGroups[island] = group;
            }
            return new SelectListItem
            {
                Value = r.Id.ToString(),
                Text = r.Name,
                Group = group,
                Selected = r.Id == Input.RegionId,
            };
        }).ToList();
        // Likewise keep the farm's current milk-supply company even if deactivated.
        MilkCompanyOptions = new SelectList(
            await _db.MilkSupplyCompanies
                .Where(c => c.IsActive || c.Id == Input.MilkSupplyCompanyId)
                .OrderBy(c => c.Name).ToListAsync(),
            "Id", "Name", Input.MilkSupplyCompanyId);
    }
}
