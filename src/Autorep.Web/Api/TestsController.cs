using System.Security.Claims;
using System.Text.Json;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Api;

// Read-only access to Machine Tests. Super-Administrator sees any test; a Company-Administrator
// sees tests done for their company in any state; a Tester sees their own tests plus COMPLETED
// tests done for the same Testing Company (the "Company tests" screen) — read-only, since every
// write goes through SyncController and is scoped to the caller's own rows.
//
// Scope is applied to the QUERY, not checked after loading, so an out-of-scope id simply doesn't
// match and reads as NotFound — the test's existence is never disclosed.
[ApiController]
[Route("api/tests")]
[Authorize]
public class TestsController : ControllerBase
{
    private readonly AutorepDbContext _db;
    public TestsController(AutorepDbContext db) => _db = db;

    /// <summary>Largest page the company list will return, whatever the caller asks for —
    /// otherwise the list is a bulk-export endpoint.</summary>
    private const int MaxTake = 100;
    private const int DefaultTake = 25;

    public record TestViewDto(
        Guid Id, string FarmName, DateTimeOffset CreatedAt, DateTimeOffset? MarkedCompleteAt,
        SyncController.ConfigDto? Config, string? PayloadJson, string? TesterName,
        // Whose test this is, decided server-side: the read-only view words itself differently for
        // your own frozen test than for a colleague's, and it must not guess from display names.
        bool IsMine);

    /// <summary>A row of the Company tests list. Header fields only — no PayloadJson (it carries
    /// the whole capture including a base64 pulsation PDF, so a page of them would be hundreds of
    /// MB) and no ClientId (a tester has no use for a colleague's sync key).</summary>
    public record CompanyTestDto(
        Guid Id, string FarmName, string? TesterName, DateTimeOffset CompletedAt,
        int Version, bool IsMine);

    public record CompanyTestsResponse(
        string? CompanyName, int Total, IReadOnlyList<CompanyTestDto> Items);

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var me = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var scoped = await ScopedAsync(ct);

        // Superseded versions stay reachable by id (the amendment history references them); they
        // are only hidden from the list.
        var test = await scoped
            .Include(t => t.Farm)
            .Include(t => t.Configuration)
            .Include(t => t.Tester)
            .FirstOrDefaultAsync(t => t.Id == id, ct);
        if (test is null) return NotFound();

        await AuditColleagueViewAsync(test, me, ct);

        Response.Headers.CacheControl = "no-store";
        return Ok(new TestViewDto(
            test.Id,
            test.Farm?.Name ?? string.Empty,
            test.CreatedAt,
            test.MarkedCompleteAt,
            test.Configuration is null ? null : SyncController.ToDto(test.Configuration),
            test.PayloadJson,
            test.Tester?.DisplayName,
            test.TesterId == me));
    }

    // The Company tests list: completed tests done for the caller's Testing Company, current
    // versions only, newest first. There is deliberately no companyId parameter — the scope comes
    // from the principal and cannot be widened from the query string.
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? q, [FromQuery] int skip = 0, [FromQuery] int take = DefaultTake,
        CancellationToken ct = default)
    {
        var me = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var companyId = await CompanyOfAsync(me, ct);

        Response.Headers.CacheControl = "no-store";

        // Not attached to a company: an empty list, not a 403 — the page needs to explain why
        // rather than render a generic failure.
        if (companyId is not { } company)
            return Ok(new CompanyTestsResponse(null, 0, []));

        var companyName = await _db.TestingCompanies
            .Where(c => c.Id == company).Select(c => c.Name).FirstOrDefaultAsync(ct);

        var query = _db.MachineTests
            .InCompany(company)
            .Where(t => t.MarkedCompleteAt != null)
            .CurrentVersionsOnly(_db);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(t =>
                (t.Farm != null && t.Farm.Name.Contains(term)) ||
                (t.Tester != null && t.Tester.DisplayName.Contains(term)));
        }

        var total = await query.CountAsync(ct);

        if (skip < 0) skip = 0;
        take = Math.Clamp(take, 1, MaxTake);

        // Projected straight to the DTO — no Include — so EF never reads PayloadJson for a list.
        // Id breaks ties so the "load more" boundary is stable when two tests share a timestamp.
        var items = await query
            .OrderByDescending(t => t.MarkedCompleteAt).ThenBy(t => t.Id)
            .Skip(skip).Take(take)
            .Select(t => new CompanyTestDto(
                t.Id,
                t.Farm != null ? t.Farm.Name : string.Empty,
                t.Tester != null ? t.Tester.DisplayName : null,
                t.MarkedCompleteAt!.Value,
                t.Version,
                t.TesterId == me))
            .ToListAsync(ct);

        return Ok(new CompanyTestsResponse(companyName, total, items));
    }

    /// <summary>
    /// The caller's readable set. Role precedence is explicit and ends in "nothing": without the
    /// final rejection, any future role that isn't one of the three would silently inherit
    /// company-wide read (this controller is not role-gated at the class level).
    /// </summary>
    private async Task<IQueryable<MachineTest>> ScopedAsync(CancellationToken ct)
    {
        if (User.IsInRole(Roles.SuperAdministrator)) return _db.MachineTests;

        var me = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (User.IsInRole(Roles.CompanyAdministrator))
        {
            // Admins see in-progress tests too — that's what the Admin list's "In progress" badge is.
            var adminCompany = await CompanyOfAsync(me, ct);
            return adminCompany is { } company
                ? _db.MachineTests.InCompany(company)
                : _db.MachineTests.Where(t => false);
        }

        if (!User.IsInRole(Roles.Tester)) return _db.MachineTests.Where(t => false);

        return _db.MachineTests.ReadableByTester(me, await CompanyOfAsync(me, ct));
    }

    private Task<Guid?> CompanyOfAsync(string? userId, CancellationToken ct) =>
        _db.Users.Where(u => u.Id == userId)
            .Select(u => u.TestingCompanyId).FirstOrDefaultAsync(ct);

    /// <summary>
    /// Records that someone opened a test they don't own. AuditInterceptor only sees writes, so a
    /// read leaves no trace otherwise — and since the report PDF is generated on-device, this is
    /// the only point at which the server can observe a colleague's test being taken. Own-test
    /// views are not recorded (they'd swamp the log without saying anything).
    /// </summary>
    private async Task AuditColleagueViewAsync(MachineTest test, string? me, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(me) || test.TesterId == me) return;

        _db.AuditEntries.Add(new AuditEntry
        {
            Actor = me,
            EntityType = nameof(MachineTest),
            EntityKey = test.Id.ToString(),
            Operation = "ViewedByColleague",
            // Who/what was read, never the payload itself — the audit store deliberately doesn't
            // retain farm PII (see AuditInterceptor.SummarizePayload).
            AfterJson = JsonSerializer.Serialize(new
            {
                ownerTesterId = test.TesterId,
                farmId = test.FarmId,
                testingCompanyId = test.TestingCompanyId,
            }),
        });
        await _db.SaveChangesAsync(ct);
    }
}
