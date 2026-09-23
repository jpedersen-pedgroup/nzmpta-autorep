using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Pages.Admin.Tests;

// Upcoming tests: which farms are due (or overdue) their next machine test, so a company can book
// the visit. Each farm is judged on its MOST RECENT completed test only; an older test's date must
// never make a farm that has since been retested look overdue. An amendment keeps the original
// test's date (the wizard enforces that), so the current version of the latest test is the source.
//
// Scope matches the All tests list: a Company Administrator sees the tests stamped with their own
// company; a Super-Administrator sees every company and can narrow to one.
public class UpcomingModel : PageModel
{
    private readonly AutorepDbContext _db;
    private readonly UserManager<Tester> _users;

    public UpcomingModel(AutorepDbContext db, UserManager<Tester> users)
    {
        _db = db;
        _users = users;
    }

    /// <summary>How far ahead to look, in days; 0 means every farm with a date. Overdue farms are
    /// always included, whatever the window.</summary>
    [BindProperty(SupportsGet = true)] public int Window { get; set; } = 90;
    [BindProperty(SupportsGet = true)] public Guid? CompanyId { get; set; }

    public static readonly (int Days, string Label)[] Windows =
        [(30, "Next 30 days"), (60, "Next 60 days"), (90, "Next 90 days"), (180, "Next 6 months"), (0, "All upcoming")];

    /// <summary>Within this many days a test counts as "due soon".</summary>
    public const int DueSoonDays = 30;

    public sealed record Row(
        Guid TestId, Guid FarmId, string FarmName, string? SupplyNumber, string? Region, string? FarmerName,
        string? ContactPhone, string? CompanyName, string? TesterName, DateTimeOffset LastTestedAt,
        DateOnly NextTestDate);

    public DateOnly Today { get; private set; }
    public IReadOnlyList<Row> Rows { get; private set; } = [];
    public int OverdueCount { get; private set; }
    public int DueSoonCount { get; private set; }
    public bool IsSuperAdmin { get; private set; }
    /// <summary>A Company Administrator not attached to a company: nothing to show, and the page
    /// says why rather than showing an empty list.</summary>
    public bool NoCompany { get; private set; }
    public List<SelectListItem> CompanyOptions { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Today = NzTime.Today;
        if (!Windows.Any(w => w.Days == Window)) Window = 90;

        Guid? scope;
        IsSuperAdmin = User.IsInRole(Roles.SuperAdministrator);
        if (IsSuperAdmin)
        {
            scope = CompanyId;
            CompanyOptions = await _db.TestingCompanies
                .OrderBy(c => c.Name)
                .Select(c => new SelectListItem(c.Name, c.Id.ToString(), c.Id == CompanyId))
                .ToListAsync();
        }
        else
        {
            CompanyId = null; // the query string can't widen a company admin's view
            scope = (await _users.GetUserAsync(User))?.TestingCompanyId;
            if (scope is null)
            {
                NoCompany = true;
                return;
            }
        }

        var horizon = Window > 0 ? Today.AddDays(Window) : (DateOnly?)null;
        var rows = await Query(_db, scope, horizon).ToListAsync();
        // Two versions of one farm's latest test can't both be current, but two different tests
        // completed at the same instant could tie; keep one row per farm.
        Rows = rows
            .GroupBy(r => r.FarmId)
            .Select(g => g.First())
            .OrderBy(r => r.NextTestDate).ThenBy(r => r.FarmName)
            .ToList();
        OverdueCount = Rows.Count(r => r.NextTestDate < Today);
        DueSoonCount = Rows.Count(r => r.NextTestDate >= Today && r.NextTestDate <= Today.AddDays(DueSoonDays));
    }

    /// <summary>Each farm's latest completed current-version test in scope that carries a next test
    /// date on or before <paramref name="horizon"/> (or any date when null). A farm is on the list
    /// only when no later completed test exists for it in the same scope. Static so a test can check
    /// it translates to SQL Server, which the in-memory provider used elsewhere can't prove.</summary>
    public static IQueryable<Row> Query(AutorepDbContext db, Guid? companyId, DateOnly? horizon)
    {
        var tests = db.MachineTests.Where(t => t.MarkedCompleteAt != null);
        if (companyId is { } company) tests = tests.InCompany(company);
        tests = tests.CurrentVersionsOnly(db);

        var latest = tests.Where(t => t.NextTestDate != null
            && !tests.Any(o => o.FarmId == t.FarmId && o.MarkedCompleteAt > t.MarkedCompleteAt));
        if (horizon is { } h) latest = latest.Where(t => t.NextTestDate <= h);

        return latest.Select(t => new Row(
            t.Id,
            t.FarmId,
            t.Farm != null ? t.Farm.Name : string.Empty,
            t.Farm != null ? t.Farm.SupplyNumber : null,
            t.Farm != null && t.Farm.Region != null ? t.Farm.Region.Name : null,
            t.Farm != null ? t.Farm.FarmerName : null,
            t.Farm != null ? t.Farm.ContactPhone : null,
            db.TestingCompanies.Where(c => c.Id == t.TestingCompanyId).Select(c => c.Name).FirstOrDefault(),
            t.Tester != null ? t.Tester.DisplayName : null,
            t.MarkedCompleteAt!.Value,
            t.NextTestDate!.Value));
    }
}
