using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Pages.Admin.Tests;

// All tests (O2 / PRD story 64): one row per test — its current version — filtered on the server by
// the chips: tester, company (Super-Administrator), farm or tester search, tested-date range, status,
// has conflicts, and whether to include deleted tests. A test's earlier versions are in its audit
// panel, not the list.
public class IndexModel : PageModel
{
    private readonly AutorepDbContext _db;
    private readonly UserManager<Tester> _users;

    public IndexModel(AutorepDbContext db, UserManager<Tester> users)
    {
        _db = db;
        _users = users;
    }

    public IList<MachineTest> Tests { get; private set; } = [];

    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    /// <summary>Farm name or supply number, tester name or email.</summary>
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? FarmId { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? CompanyId { get; set; }
    [BindProperty(SupportsGet = true)] public string? TesterId { get; set; }
    /// <summary>Tested on or after / on or before these New Zealand dates.</summary>
    [BindProperty(SupportsGet = true)] public DateOnly? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? To { get; set; }
    /// <summary><see cref="AdminTestQuery.Complete"/>, <see cref="AdminTestQuery.InProgress"/>, or empty.</summary>
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }
    /// <summary>Only tests that had a sync conflict (two versions replacing the same one).</summary>
    [BindProperty(SupportsGet = true)] public bool HasConflicts { get; set; }
    /// <summary>Include soft-deleted tests (marked as such). They're hidden by default.</summary>
    [BindProperty(SupportsGet = true)] public bool ShowDeleted { get; set; }

    public string? FarmName { get; private set; }
    public List<SelectListItem> CompanyOptions { get; private set; } = [];
    public List<SelectListItem> TesterOptions { get; private set; } = [];
    /// <summary>The tests on this page whose history includes a sync conflict.</summary>
    public HashSet<Guid> ConflictedIds { get; private set; } = [];

    /// <summary>The active filters as removable chips: what each says, and the query value it removes.</summary>
    public IReadOnlyList<(string Label, string Param)> Chips { get; private set; } = [];

    public bool HasFilter => Chips.Any(c => c.Param != "farmId");
    public int PageSize { get; } = 50;
    public int TotalCount { get; private set; }
    public int TotalPages => TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public async Task OnGetAsync()
    {
        // A Company Administrator may only see tests performed by Testers in
        // their own testing company (the company filter is pinned for them);
        // a Super-Administrator sees everything and may filter by any company.
        var superAdmin = User.IsInRole(Roles.SuperAdministrator);
        Guid? scopeCompanyId;
        string? meId = null;
        if (!superAdmin)
        {
            var me = await _users.GetUserAsync(User);
            meId = me?.Id;
            scopeCompanyId = me?.TestingCompanyId;
            if (scopeCompanyId is null) return; // an admin not attached to a company owns no tests
            CompanyId = null; // ignore any attempt to widen the view via the query string
        }
        else
        {
            scopeCompanyId = CompanyId;
            CompanyOptions = await _db.TestingCompanies
                .OrderBy(c => c.Name)
                .Select(c => new SelectListItem(c.Name, c.Id.ToString(), c.Id == CompanyId))
                .ToListAsync();
        }
        if (Status is not (AdminTestQuery.Complete or AdminTestQuery.InProgress)) Status = null;

        // Tester dropdown, narrowed to the scoped company when one applies.
        IQueryable<Tester> testerQuery = _db.Users;
        if (scopeCompanyId is not null)
            testerQuery = testerQuery.Where(u => u.TestingCompanyId == scopeCompanyId);
        TesterOptions = await testerQuery
            .OrderBy(u => u.DisplayName).ThenBy(u => u.Email)
            .Select(u => new SelectListItem(
                u.DisplayName != "" ? u.DisplayName : (u.Email ?? u.Id),
                u.Id,
                u.Id == TesterId))
            .ToListAsync();

        // Deep-link from a farm's details page: scope to that farm's tests. Resolve the heading's
        // farm name through the company scope for non-super-admins, so a guessed farm id can't
        // disclose another company's farm name.
        if (FarmId is not null)
        {
            var farmQuery = _db.Farms.Where(f => f.Id == FarmId);
            if (!superAdmin)
                farmQuery = farmQuery.InCompanyScope(_db, scopeCompanyId, meId);
            FarmName = await farmQuery.Select(f => f.Name).FirstOrDefaultAsync();
        }

        var filter = new AdminTestQuery.Filter(scopeCompanyId, TesterId, FarmId, Q, From, To, Status, HasConflicts, ShowDeleted);
        var query = AdminTestQuery.Apply(_db, filter);

        TotalCount = await query.CountAsync();
        if (PageNumber < 1) PageNumber = 1;

        Tests = await AdminTestQuery.Newest(query)
            .Include(t => t.Tester)
            .Include(t => t.Farm)
            .Skip((PageNumber - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();
        ConflictedIds = await AdminTestQuery.ConflictedAsync(_db, Tests);
        Chips = ChipsFor(superAdmin);
    }

    private List<(string, string)> ChipsFor(bool superAdmin)
    {
        var chips = new List<(string, string)>();
        if (FarmId is not null) chips.Add(($"Farm: {FarmName ?? "this farm"}", "farmId"));
        if (!string.IsNullOrWhiteSpace(Q)) chips.Add(($"Search: {Q.Trim()}", "q"));
        if (superAdmin && CompanyId is { } company)
            chips.Add(($"Company: {CompanyOptions.FirstOrDefault(o => o.Value == company.ToString())?.Text ?? "—"}", "companyId"));
        if (!string.IsNullOrEmpty(TesterId))
            chips.Add(($"Tester: {TesterOptions.FirstOrDefault(o => o.Value == TesterId)?.Text ?? "—"}", "testerId"));
        if (From is { } from) chips.Add(($"Tested from {from:d MMM yyyy}", "from"));
        if (To is { } to) chips.Add(($"Tested to {to:d MMM yyyy}", "to"));
        if (Status is not null) chips.Add((Status == AdminTestQuery.Complete ? "Complete" : "In progress", "status"));
        if (HasConflicts) chips.Add(("Has conflicts", "hasConflicts"));
        if (ShowDeleted) chips.Add(("Including deleted", "showDeleted"));
        return chips;
    }

    /// <summary>The page's query string — every filter, minus <paramref name="without"/>, at
    /// <paramref name="page"/> — for the chips' remove links and the pager.</summary>
    public Dictionary<string, string> RouteWith(string? without = null, int? page = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["q"] = string.IsNullOrWhiteSpace(Q) ? null : Q.Trim(),
            ["farmId"] = FarmId?.ToString(),
            ["companyId"] = CompanyId?.ToString(),
            ["testerId"] = string.IsNullOrEmpty(TesterId) ? null : TesterId,
            ["from"] = From?.ToString("yyyy-MM-dd"),
            ["to"] = To?.ToString("yyyy-MM-dd"),
            ["status"] = Status,
            ["hasConflicts"] = HasConflicts ? "true" : null,
            ["showDeleted"] = ShowDeleted ? "true" : null,
            ["pageNumber"] = page is > 1 ? page.Value.ToString() : null,
        };
        if (without is not null) values.Remove(without);
        return values.Where(v => v.Value is not null).ToDictionary(v => v.Key, v => v.Value!);
    }
}

/// <summary>
/// The admin test list's query: one row per test (its current version) in the caller's scope,
/// narrowed by the filter chips. Static so a test can check it translates to SQL Server, which the
/// in-memory provider used elsewhere can't prove.
/// </summary>
public static class AdminTestQuery
{
    public const string Complete = "complete";
    public const string InProgress = "in-progress";

    public sealed record Filter(
        Guid? CompanyId, string? TesterId, Guid? FarmId, string? Search,
        DateOnly? From, DateOnly? To, string? Status, bool HasConflicts, bool ShowDeleted);

    public static IQueryable<MachineTest> Apply(AutorepDbContext db, Filter f)
    {
        // Scoped on the company stamped on the test, not the owner's current company, so admins and
        // testers share one definition of "our tests" — and a tester who transfers in doesn't bring
        // their previous employer's work with them.
        IQueryable<MachineTest> query = db.MachineTests;
        if (f.CompanyId is { } company) query = query.InCompany(company);
        query = query.CurrentVersionsOnly(db);
        if (!f.ShowDeleted) query = query.Where(t => !t.IsDeleted);
        if (!string.IsNullOrEmpty(f.TesterId)) query = query.Where(t => t.TesterId == f.TesterId);
        if (f.FarmId is { } farm) query = query.Where(t => t.FarmId == farm);

        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var term = f.Search.Trim();
            query = query.Where(t =>
                (t.Farm != null && (t.Farm.Name.Contains(term) || (t.Farm.SupplyNumber != null && t.Farm.SupplyNumber.Contains(term))))
                || (t.Tester != null && (t.Tester.DisplayName.Contains(term) || (t.Tester.Email != null && t.Tester.Email.Contains(term)))));
        }

        // The tested date: when the test was signed off (an administrator's version keeps that date),
        // or when it was started, while it's in progress. New Zealand calendar days. A bound at either
        // end of the calendar excludes nothing, and has no instant to compare with (the day after the
        // last one doesn't exist; the first one's NZ midnight is before year 1 in UTC): it's left off.
        if (f.From is { } from && from > DateOnly.MinValue)
        {
            var start = NzTime.StartOf(from);
            query = query.Where(t => (t.MarkedCompleteAt ?? t.CreatedAt) >= start);
        }
        if (f.To is { } to && to < DateOnly.MaxValue)
        {
            var end = NzTime.StartOf(to.AddDays(1));
            query = query.Where(t => (t.MarkedCompleteAt ?? t.CreatedAt) < end);
        }

        if (f.Status == Complete) query = query.Where(t => t.MarkedCompleteAt != null);
        else if (f.Status == InProgress) query = query.Where(t => t.MarkedCompleteAt == null);

        // Any collision in the test's history, merged or still pending (SyncConflict, by lineage).
        if (f.HasConflicts)
            query = query.Where(t => db.SyncConflicts.Any(c =>
                c.TesterId == t.TesterId && c.RootClientId == (t.RootClientId ?? t.ClientId)));
        return query;
    }

    /// <summary>Most recently tested first; the id breaks ties so pages don't overlap.</summary>
    public static IQueryable<MachineTest> Newest(IQueryable<MachineTest> query) =>
        query.OrderByDescending(t => t.MarkedCompleteAt ?? t.CreatedAt).ThenBy(t => t.Id);

    /// <summary>Which of these tests had a sync conflict, in one query.</summary>
    public static async Task<HashSet<Guid>> ConflictedAsync(AutorepDbContext db, IEnumerable<MachineTest> tests)
    {
        var page = tests.ToList();
        var keys = page.Select(TestLineage.KeyOf).Where(k => k != null).Distinct().ToList();
        if (keys.Count == 0) return [];
        var conflicted = await db.SyncConflicts
            .Where(c => keys.Contains(c.RootClientId))
            .Select(c => new { c.TesterId, c.RootClientId })
            .ToListAsync();
        return page
            .Where(t => conflicted.Any(c => c.TesterId == t.TesterId && c.RootClientId == TestLineage.KeyOf(t)))
            .Select(t => t.Id)
            .ToHashSet();
    }
}
