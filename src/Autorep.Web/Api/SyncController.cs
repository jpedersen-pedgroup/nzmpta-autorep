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
        string? PayloadJson);

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

    // Push: upsert by ClientId (idempotent), creating/linking the Farm by name.
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

    private static ConfigDto ToDto(MachineConfiguration c) => new(
        c.PlantType.ToString(), c.PlantSize, c.ClusterCount, c.HerdSize, c.AtmosPressureSeaLevel,
        c.LastBmcc, c.MilklineSize, c.FlushingPulsationSystem,
        c.PulsatorBrand, c.PulsatorModel, c.PulsatorConfiguration, c.PulsatorCount,
        c.ClawModel, c.ShellModel, c.LinerModel, c.BackLiner, c.LinerVented,
        c.NumberOfVacuumPumps, c.PumpLubrication.ToString(), c.VsdFitted, c.IsoPortsAvailable,
        c.HasPulsatorStopSystem, c.HasAcr, c.HasBailGates, c.HasMilkMeters,
        c.HasTeatSprayer, c.HasBackingGate, c.HasReleaserPump);
}
