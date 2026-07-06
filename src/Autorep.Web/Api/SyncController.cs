using System.Security.Claims;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Api;

// Tester sync surface. Tests are created/edited on-device (IndexedDB) and pushed here,
// upserting by ClientId so retries are safe; the list endpoint lets a Device pull the
// Tester's tests back (new device, or to refresh). Carries the Machine Configuration now;
// the richer capture payload (visual faults, readings) + the Sync Reconciliation Engine
// follow in later phases.
[ApiController]
[Route("api/sync")]
[Authorize(Roles = Roles.Tester)]
public class SyncController : ControllerBase
{
    private readonly AutorepDbContext _db;

    public SyncController(AutorepDbContext db) => _db = db;

    public record ConfigDto(
        string PlantType, string? PlantSize, int ClusterCount, int? HerdSize, int? AtmosPressureSeaLevel,
        string? LastBmcc, string? MilklineSize, bool FlushingPulsationSystem,
        string? PulsatorBrand, string? PulsatorModel, string? PulsatorConfiguration, int PulsatorCount,
        string? ClawModel, string? ShellModel, string? LinerModel, string? BackLiner, bool LinerVented,
        int NumberOfVacuumPumps, string PumpLubrication, bool VsdFitted, bool IsoPortsAvailable,
        bool HasPulsatorStopSystem, bool HasAcr, bool HasBailGates, bool HasMilkMeters,
        bool HasTeatSprayer, bool HasBackingGate, bool HasReleaserPump);

    public record UploadTestRequest(
        Guid ClientId, string FarmName, string? Notes,
        DateTimeOffset? MarkedCompleteAt, DateTimeOffset? CreatedAt, ConfigDto? Config,
        string? PayloadJson,
        // Farm identity for linking (added later, so optional for older queued payloads):
        // the FarmId the wizard was started with, plus the snapshot fields used to match an
        // existing farm when there is no usable id.
        Guid? FarmId = null, string? FarmSupplyNumber = null, string? FarmMilkCompanyName = null);

    public record TestSummaryDto(
        Guid ClientId, string FarmName, DateTimeOffset CreatedAt,
        DateTimeOffset? MarkedCompleteAt, ConfigDto? Config, string? PayloadJson);

    // Pull: the Tester's tests (header + config), newest first.
    [HttpGet("tests")]
    public async Task<IActionResult> ListTests(CancellationToken ct)
    {
        var testerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var tests = await _db.MachineTests
            .Include(t => t.Farm)
            .Include(t => t.Configuration)
            .Where(t => t.TesterId == testerId && t.ClientId != null)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

        var dtos = tests.Select(t => new TestSummaryDto(
            t.ClientId!.Value,
            t.Farm?.Name ?? string.Empty,
            t.CreatedAt,
            t.MarkedCompleteAt,
            t.Configuration is null ? null : ToDto(t.Configuration),
            t.PayloadJson));

        return Ok(dtos);
    }

    // Push: upsert by ClientId (idempotent), linking the Farm by id / farm identity within the
    // tester's company scope (see ResolveFarmAsync), creating a company-tagged farm if needed.
    [HttpPost("tests")]
    public async Task<IActionResult> UploadTest([FromBody] UploadTestRequest req, CancellationToken ct)
    {
        var testerId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("No NameIdentifier claim on principal.");

        if (string.IsNullOrWhiteSpace(req.FarmName))
            return BadRequest(new { error = "FarmName is required" });

        // Scope the upsert to the caller's own tests: a ClientId belonging to another tester must
        // never match here (otherwise tester A could overwrite tester B's test — IDOR). Combined
        // with the unique (TesterId, ClientId) index, a foreign ClientId falls through to create.
        var existing = await _db.MachineTests
            .Include(t => t.Configuration)
            .FirstOrDefaultAsync(t => t.ClientId == req.ClientId && t.TesterId == testerId, ct);

        if (existing is not null)
        {
            existing.Notes = req.Notes;
            existing.MarkedCompleteAt = req.MarkedCompleteAt;
            existing.PayloadJson = req.PayloadJson;
            ApplyConfig(existing, req.Config);
            await _db.SaveChangesAsync(ct);
            return Ok(new { id = existing.Id, status = "updated" });
        }

        var farm = await ResolveFarmAsync(req, testerId, ct);

        var test = new MachineTest
        {
            ClientId = req.ClientId,
            TesterId = testerId,
            FarmId = farm.Id,
            Farm = farm,
            Notes = req.Notes,
            MarkedCompleteAt = req.MarkedCompleteAt,
            CreatedAt = req.CreatedAt ?? DateTimeOffset.UtcNow,
            PayloadJson = req.PayloadJson,
        };
        ApplyConfig(test, req.Config);
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
            .Include(t => t.Configuration)
            .FirstOrDefaultAsync(t => t.Id == id && t.TesterId == testerId, ct);
        if (test is null) return NotFound();

        // Project to the DTO rather than returning the raw entity (avoids leaking the Farm
        // navigation and any future entity members through the sync surface).
        return Ok(new TestSummaryDto(
            test.ClientId ?? Guid.Empty,
            test.Farm?.Name ?? string.Empty,
            test.CreatedAt,
            test.MarkedCompleteAt,
            test.Configuration is null ? null : ToDto(test.Configuration),
            test.PayloadJson));
    }

    // Links the synced test to a Farm, always within the tester's company scope so a sync push
    // can never attach a test to (and thereby gain visibility of) another company's farm:
    // 1. by the device's FarmId (set when the wizard was started from the picker), if in scope;
    // 2. else by farm identity — name + supply number + milk processor — within scope, so two
    //    companies' same-named farms stay separate while retries still find the right farm;
    // 3. else a new farm is created, tagged with the syncing tester's company (matching farms
    //    created via the New-test "add farm" modal).
    // Deliberately does NOT filter on Farm.IsActive: a test may have been started in the field
    // before the farm was deactivated, and the completed work must still land on the right farm
    // rather than be stranded or duplicated. (New tests can't be *started* on inactive farms —
    // the New-test page enforces that.)
    private async Task<Farm> ResolveFarmAsync(UploadTestRequest req, string testerId, CancellationToken ct)
    {
        var companyId = await _db.Users.Where(u => u.Id == testerId)
            .Select(u => u.TestingCompanyId).FirstOrDefaultAsync(ct);

        if (req.FarmId is not null)
        {
            var byId = await _db.Farms.Where(f => f.Id == req.FarmId)
                .InCompanyScope(_db, companyId, testerId)
                .FirstOrDefaultAsync(ct);
            if (byId is not null) return byId;
        }

        var name = req.FarmName.Trim();
        var supply = Clean(req.FarmSupplyNumber);
        var milk = Clean(req.FarmMilkCompanyName);

        var byIdentity = _db.Farms.InCompanyScope(_db, companyId, testerId)
            .Where(f => f.Name == name && f.SupplyNumber == supply);
        byIdentity = milk is null
            ? byIdentity.Where(f => f.MilkSupplyCompanyId == null)
            : byIdentity.Where(f => f.MilkSupplyCompany != null && f.MilkSupplyCompany.Name == milk);
        // Oldest first so retries pick the same row even if duplicate identities are in scope.
        var match = await byIdentity.OrderBy(f => f.CreatedAt).FirstOrDefaultAsync(ct);
        if (match is not null) return match;

        var milkCompanyId = milk is null
            ? null
            : await _db.MilkSupplyCompanies.Where(c => c.Name == milk)
                .Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
        var farm = new Farm
        {
            Name = name,
            SupplyNumber = supply,
            MilkSupplyCompanyId = milkCompanyId,
            CreatedByTestingCompanyId = companyId,
        };
        _db.Farms.Add(farm);
        return farm;
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static void ApplyConfig(MachineTest test, ConfigDto? dto)
    {
        if (dto is null) return;

        var cfg = test.Configuration ?? new MachineConfiguration();
        cfg.PlantType = Enum.TryParse<PlantType>(dto.PlantType, out var pt) ? pt : PlantType.Other;
        cfg.PlantSize = dto.PlantSize;
        cfg.ClusterCount = dto.ClusterCount;
        cfg.HerdSize = dto.HerdSize;
        cfg.AtmosPressureSeaLevel = dto.AtmosPressureSeaLevel;
        cfg.LastBmcc = dto.LastBmcc;
        cfg.MilklineSize = dto.MilklineSize;
        cfg.FlushingPulsationSystem = dto.FlushingPulsationSystem;
        cfg.PulsatorBrand = dto.PulsatorBrand;
        cfg.PulsatorModel = dto.PulsatorModel;
        cfg.PulsatorConfiguration = dto.PulsatorConfiguration;
        cfg.PulsatorCount = dto.PulsatorCount;
        cfg.ClawModel = dto.ClawModel;
        cfg.ShellModel = dto.ShellModel;
        cfg.LinerModel = dto.LinerModel;
        cfg.BackLiner = dto.BackLiner;
        cfg.LinerVented = dto.LinerVented;
        cfg.NumberOfVacuumPumps = dto.NumberOfVacuumPumps;
        cfg.PumpLubrication = Enum.TryParse<PumpLubrication>(dto.PumpLubrication, out var pl) ? pl : PumpLubrication.Other;
        cfg.VsdFitted = dto.VsdFitted;
        cfg.IsoPortsAvailable = dto.IsoPortsAvailable;
        cfg.HasPulsatorStopSystem = dto.HasPulsatorStopSystem;
        cfg.HasAcr = dto.HasAcr;
        cfg.HasBailGates = dto.HasBailGates;
        cfg.HasMilkMeters = dto.HasMilkMeters;
        cfg.HasTeatSprayer = dto.HasTeatSprayer;
        cfg.HasBackingGate = dto.HasBackingGate;
        cfg.HasReleaserPump = dto.HasReleaserPump;
        cfg.UpdatedAt = DateTimeOffset.UtcNow;

        test.Configuration = cfg;
    }

    internal static ConfigDto ToDto(MachineConfiguration c) => new(
        c.PlantType.ToString(), c.PlantSize, c.ClusterCount, c.HerdSize, c.AtmosPressureSeaLevel,
        c.LastBmcc, c.MilklineSize, c.FlushingPulsationSystem,
        c.PulsatorBrand, c.PulsatorModel, c.PulsatorConfiguration, c.PulsatorCount,
        c.ClawModel, c.ShellModel, c.LinerModel, c.BackLiner, c.LinerVented,
        c.NumberOfVacuumPumps, c.PumpLubrication.ToString(), c.VsdFitted, c.IsoPortsAvailable,
        c.HasPulsatorStopSystem, c.HasAcr, c.HasBailGates, c.HasMilkMeters,
        c.HasTeatSprayer, c.HasBackingGate, c.HasReleaserPump);
}
