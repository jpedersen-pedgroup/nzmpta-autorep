using System.Security.Claims;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Api;

// Phase 1 walking-skeleton sync surface. Accepts a single Test upload
// from a Tester's Device, upserting by ClientId so retries are safe.
// Phase 2-4 will grow the payload to the full Machine Test shape;
// Phase 9 wires this through the Sync Reconciliation Engine for proper
// field-level merge.
[ApiController]
[Route("api/sync")]
[Authorize(Roles = Roles.Tester)]
public class SyncController : ControllerBase
{
    private readonly AutorepDbContext _db;

    public SyncController(AutorepDbContext db) => _db = db;

    public record UploadTestRequest(
        Guid ClientId,
        string FarmName,
        string? Notes,
        DateTimeOffset? MarkedCompleteAt);

    [HttpPost("tests")]
    public async Task<IActionResult> UploadTest([FromBody] UploadTestRequest req, CancellationToken ct)
    {
        var testerId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("No NameIdentifier claim on principal.");

        if (string.IsNullOrWhiteSpace(req.FarmName))
            return BadRequest(new { error = "FarmName is required" });

        // Upsert by ClientId: idempotent so resync after a partial failure is safe.
        var existing = await _db.MachineTests
            .FirstOrDefaultAsync(t => t.ClientId == req.ClientId, ct);

        if (existing is not null)
        {
            existing.Notes = req.Notes;
            existing.MarkedCompleteAt = req.MarkedCompleteAt;
            await _db.SaveChangesAsync(ct);
            return Ok(new { id = existing.Id, status = "updated" });
        }

        var farm = await _db.Farms.FirstOrDefaultAsync(f => f.Name == req.FarmName, ct);
        if (farm is null)
        {
            farm = new Farm { Name = req.FarmName };
            _db.Farms.Add(farm);
        }

        var test = new MachineTest
        {
            ClientId = req.ClientId,
            TesterId = testerId,
            FarmId = farm.Id,
            Farm = farm,
            Notes = req.Notes,
            MarkedCompleteAt = req.MarkedCompleteAt
        };
        _db.MachineTests.Add(test);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetTest), new { id = test.Id },
            new { id = test.Id, status = "created" });
    }

    [HttpGet("tests/{id:guid}")]
    public async Task<IActionResult> GetTest(Guid id, CancellationToken ct)
    {
        var testerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var test = await _db.MachineTests
            .Include(t => t.Farm)
            .FirstOrDefaultAsync(t => t.Id == id && t.TesterId == testerId, ct);
        return test is null ? NotFound() : Ok(test);
    }
}
