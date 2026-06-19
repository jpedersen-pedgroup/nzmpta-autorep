using System.Security.Claims;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Api;

// Read-only fetch of a single Machine Test, for the admin "view any test" screen (and a tester's
// own tests). Super-Administrator sees any test; a Company-Administrator sees tests by testers in
// their company; a Tester sees only their own. Returns the same config + payload shape the sync
// pull uses, so the client renders it through the read-only wizard (legacy payloads are adapted
// client-side). NotFound is returned for out-of-scope ids so their existence isn't disclosed.
[ApiController]
[Route("api/tests")]
[Authorize]
public class TestsController : ControllerBase
{
    private readonly AutorepDbContext _db;
    public TestsController(AutorepDbContext db) => _db = db;

    public record TestViewDto(
        Guid Id, string FarmName, DateTimeOffset CreatedAt, DateTimeOffset? MarkedCompleteAt,
        SyncController.ConfigDto? Config, string? PayloadJson, string? TesterName);

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var test = await _db.MachineTests
            .Include(t => t.Farm)
            .Include(t => t.Configuration)
            .Include(t => t.Tester)
            .FirstOrDefaultAsync(t => t.Id == id, ct);
        if (test is null) return NotFound();
        if (!await CanViewAsync(test, ct)) return NotFound();

        return Ok(new TestViewDto(
            test.Id,
            test.Farm?.Name ?? string.Empty,
            test.CreatedAt,
            test.MarkedCompleteAt,
            test.Configuration is null ? null : SyncController.ToDto(test.Configuration),
            test.PayloadJson,
            test.Tester?.DisplayName));
    }

    private async Task<bool> CanViewAsync(MachineTest test, CancellationToken ct)
    {
        if (User.IsInRole(Roles.SuperAdministrator)) return true;

        var me = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (User.IsInRole(Roles.CompanyAdministrator))
        {
            var myCompany = await _db.Users.Where(u => u.Id == me)
                .Select(u => u.TestingCompanyId).FirstOrDefaultAsync(ct);
            if (myCompany is null) return false;
            var ownerCompany = await _db.Users.Where(u => u.Id == test.TesterId)
                .Select(u => u.TestingCompanyId).FirstOrDefaultAsync(ct);
            return ownerCompany == myCompany;
        }

        return test.TesterId == me; // Tester: own tests only
    }
}
