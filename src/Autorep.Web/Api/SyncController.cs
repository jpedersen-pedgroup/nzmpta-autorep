using System.Globalization;
using System.Security.Claims;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Autorep.Web.Services;
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
    private readonly FarmReviewNotifier _reviewNotifier;

    public SyncController(AutorepDbContext db, FarmReviewNotifier reviewNotifier)
    {
        _db = db;
        _reviewNotifier = reviewNotifier;
    }

    public record ConfigDto(
        string PlantType, string? PlantSize, int ClusterCount, int? HerdSize, int? AtmosPressureSeaLevel,
        string? LastBmcc, string? MilklineSize, bool FlushingPulsationSystem,
        string? PulsatorBrand, string? PulsatorModel, string? PulsatorConfiguration, int PulsatorCount,
        string? ClawModel, string? ShellModel, string? LinerModel, string? BackLiner, bool LinerVented,
        int NumberOfVacuumPumps, string PumpLubrication, bool VsdFitted, bool IsoPortsAvailable,
        bool HasPulsatorStopSystem, bool HasAcr, bool HasBailGates, bool HasMilkMeters,
        bool HasTeatSprayer, bool HasBackingGate, bool HasReleaserPump,
        // Pump details arrived later, so they are optional: a device still queueing the older shape
        // pushes successfully, and null means "this client doesn't know about them" — the stored
        // rows are left alone rather than wiped by an older device re-syncing the same test.
        IReadOnlyList<VacuumPumpDetail>? VacuumPumps = null,
        IReadOnlyList<ReleaserPumpDetail>? ReleaserPumps = null,
        // Regulators arrived later again, on the same terms. A client that knows about them always
        // sends the list, so the suitability answer (where null means "not answered") is only
        // taken alongside it.
        IReadOnlyList<RegulatorDetail>? Regulators = null,
        bool? RegulatorsSuitable = null);

    public record UploadTestRequest(
        Guid ClientId, string FarmName, string? Notes,
        DateTimeOffset? MarkedCompleteAt, DateTimeOffset? CreatedAt, ConfigDto? Config,
        string? PayloadJson,
        // Farm identity for linking (added later, so optional for older queued payloads):
        // the FarmId the wizard was started with, plus the snapshot fields used to match an
        // existing farm when there is no usable id.
        Guid? FarmId = null, string? FarmSupplyNumber = null, string? FarmMilkCompanyName = null,
        // Version chain, mirrored out of PayloadJson into columns so the server can filter
        // superseded versions without materialising the payload. Optional: a device queued
        // before these existed still pushes successfully and lands as v1.
        int? Version = null, Guid? SupersedesClientId = null,
        // Mirrored out of PayloadJson for the Upcoming tests page. Null from a device that predates
        // it leaves a stored date alone.
        DateOnly? NextTestDate = null);

    public record TestSummaryDto(
        Guid ClientId, string FarmName, DateTimeOffset CreatedAt,
        DateTimeOffset? MarkedCompleteAt, ConfigDto? Config, string? PayloadJson);

    /// <summary>Pull envelope. Watermark is stored by the Device and sent back as `since` on its
    /// next pull — server clock on both sides, so device clock skew is irrelevant. <c>Next</c> is the
    /// cursor for the following page when the device asked for pages (<c>limit</c>); null on the
    /// last page, and always null for a device that didn't.</summary>
    public record PullResponse(DateTimeOffset Watermark, IReadOnlyList<TestSummaryDto> Tests, string? Next = null);

    /// <summary>The most a device can ask for in one page.</summary>
    internal const int MaxPageSize = 200;

    // The watermark is deliberately LAGGED behind now. UpdatedAt is stamped app-side shortly
    // BEFORE the row's transaction commits, so a pull racing a concurrent push could capture
    // "now" above a stamp whose row isn't visible to its query yet — and that row would sit
    // below every future watermark, permanently skipped. Returning (now − lag) instead means
    // such a row is always above the watermark and arrives on the next pull. The cost is that
    // every pull re-delivers rows written within the lag window — harmless, because the pull
    // upserts and the device's local copy wins. The lag must exceed the longest stamp-to-commit
    // gap, which is bounded by the SQL command timeout (30s default; no retry strategy is
    // configured on this context).
    private static readonly TimeSpan WatermarkLag = TimeSpan.FromSeconds(120);

    // Pull: the Tester's tests (header + config), newest first. With ?since= (the Watermark of
    // the previous pull) only tests written since then are returned — a delta, not the full set.
    //
    // A device's FIRST pull is the tester's whole history, every row carrying its full payload, so
    // a current device asks for it in pages (?limit=, then ?cursor= from each answer's Next) and
    // stores each page as it lands: the newest tests are on screen while the tail is still coming,
    // and an interrupted pull resumes rather than starting over. It keeps the FIRST page's
    // watermark, so anything written while it paged comes back on the next pull. The cursor is an
    // offset over a total order (created, then id): rows are never deleted and a row never leaves
    // the since-window once in it, so the only thing that can shift under a cursor is a row arriving
    // at the top — which re-delivers a row (harmless: the pull upserts), never skips one. Without
    // ?limit= the answer is the whole set, exactly as a device that predates paging expects.
    //
    // ?attachments=omit leaves the pulsation analyser PDFs' bytes on the server (marking each
    // attachment as held there): the device fetches one back from tests/{clientId}/pulsation-pdf
    // when it prints, rather than storing every PDF the tester ever attached.
    [HttpGet("tests")]
    public async Task<IActionResult> ListTests(
        [FromQuery] DateTimeOffset? since, [FromQuery] int? limit, [FromQuery] string? cursor,
        [FromQuery] string? attachments, CancellationToken ct)
    {
        var testerId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var watermark = DateTimeOffset.UtcNow - WatermarkLag;

        var offset = 0;
        if (cursor is not null
            && (!int.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out offset) || offset < 0))
            return BadRequest(new { error = "Unrecognised cursor — start the pull again." });

        var query = _db.MachineTests
            .Include(t => t.Farm)
            .Include(t => t.Configuration)
            .Where(t => t.TesterId == testerId && t.ClientId != null);
        if (since is not null) query = query.Where(t => t.UpdatedAt > since);
        query = query.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id).Skip(offset);

        string? next = null;
        List<MachineTest> tests;
        if (limit is { } requested)
        {
            var size = Math.Clamp(requested, 1, MaxPageSize);
            tests = await query.Take(size + 1).ToListAsync(ct);
            if (tests.Count > size)
            {
                tests.RemoveAt(size);
                next = (offset + size).ToString(CultureInfo.InvariantCulture);
            }
        }
        else
        {
            tests = await query.ToListAsync(ct);
        }

        var omitAttachments = string.Equals(attachments, "omit", StringComparison.OrdinalIgnoreCase);
        var dtos = tests.Select(t => new TestSummaryDto(
            t.ClientId!.Value,
            t.Farm?.Name ?? string.Empty,
            t.CreatedAt,
            t.MarkedCompleteAt,
            t.Configuration is null ? null : ToDto(t.Configuration),
            omitAttachments ? PulsationPayload.WithoutBytes(t.PayloadJson) : t.PayloadJson));

        return Ok(new PullResponse(watermark, dtos.ToList(), next));
    }

    // The pulsation analyser PDF attached to one of the tester's own tests, by the device's id for
    // it — for a device that has dropped its copy (or never pulled the bytes) and is printing.
    [HttpGet("tests/{clientId:guid}/pulsation-pdf")]
    public async Task<IActionResult> GetPulsationPdf(Guid clientId, CancellationToken ct)
    {
        var testerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var payload = await _db.MachineTests
            .Where(t => t.TesterId == testerId && t.ClientId == clientId)
            .Select(t => t.PayloadJson)
            .FirstOrDefaultAsync(ct);
        var base64 = PulsationPayload.Base64(payload);
        if (base64 is null) return NotFound();

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return NotFound();
        }
        Response.Headers.CacheControl = "no-store";
        return File(bytes, "application/pdf", PulsationPayload.FileName(payload) ?? "pulsation-analyser.pdf");
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

        // A device that dropped its copy of the analyser PDF re-sends the test with a pointer in its
        // place; put the bytes back from the copy already stored, so a re-push can never lose them.
        var payloadJson = await WithAttachmentBytesAsync(req.PayloadJson, testerId, req.ClientId, req.SupersedesClientId, ct);

        if (existing is not null)
        {
            existing.Notes = req.Notes;
            existing.MarkedCompleteAt = req.MarkedCompleteAt;
            existing.PayloadJson = payloadJson;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            existing.Version = req.Version ?? existing.Version;
            existing.SupersedesClientId = req.SupersedesClientId ?? existing.SupersedesClientId;
            existing.NextTestDate = req.NextTestDate ?? existing.NextTestDate;
            // TestingCompanyId is deliberately NOT re-stamped: it records the company the work was
            // done for. Re-deriving it here would drag a tester's old tests into their new company
            // the first time they re-synced after a transfer.
            ApplyConfig(existing, req.Config);
            await _db.SaveChangesAsync(ct);
            return Ok(new { id = existing.Id, status = "updated" });
        }

        var companyId = await CompanyOfAsync(testerId, ct);
        var farm = await ResolveFarmAsync(req, testerId, companyId, ct);
        // Whether ResolveFarmAsync minted a new farm row (vs linking an existing one) — checked
        // before SaveChanges flips the state, so the review notification fires exactly once.
        var farmCreated = _db.Entry(farm).State == EntityState.Added;

        var test = new MachineTest
        {
            ClientId = req.ClientId,
            TesterId = testerId,
            TestingCompanyId = companyId,
            FarmId = farm.Id,
            Farm = farm,
            Notes = req.Notes,
            MarkedCompleteAt = req.MarkedCompleteAt,
            CreatedAt = req.CreatedAt ?? DateTimeOffset.UtcNow,
            PayloadJson = payloadJson,
            Version = req.Version ?? 1,
            SupersedesClientId = req.SupersedesClientId,
            NextTestDate = req.NextTestDate,
        };
        ApplyConfig(test, req.Config);
        _db.MachineTests.Add(test);
        await _db.SaveChangesAsync(ct);

        if (farmCreated && farm.PendingReviewSince is not null)
            await _reviewNotifier.NotifyPendingFarmAsync(farm,
                $"{Request.Scheme}://{Request.Host}/Admin/Farms/Edit/{farm.Id}", ct);

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
    // rather than be stranded or duplicated. (New tests aren't *started* on inactive farms: the
    // New-test picker offers only the device's cached farm book, which holds active farms only.
    // A farm deactivated since the device last synced can still be picked offline — by design,
    // the test lands on it here rather than being lost.)
    private async Task<Farm> ResolveFarmAsync(
        UploadTestRequest req, string testerId, Guid? companyId, CancellationToken ct)
    {
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
            CreatedByTesterId = testerId,
            // Field-created farms go under review by a Company Administrator (matching the
            // New-test modal); a user who also holds an administrator role skips it.
            PendingReviewSince =
                User.IsInRole(Roles.CompanyAdministrator) || User.IsInRole(Roles.SuperAdministrator)
                    ? null
                    : DateTimeOffset.UtcNow,
        };
        _db.Farms.Add(farm);
        return farm;
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>
    /// The incoming payload, with the analyser PDF's bytes restored when the device sent a pointer
    /// instead: from the test the pointer names, else this test's own stored copy, else the version
    /// it supersedes (a new version carries the original's attachment). Only ever the caller's own
    /// tests. If no stored copy holds the bytes the pointer is kept as sent — the device only drops
    /// its copy once the server has confirmed the bytes, so that would mean they were never here.
    /// </summary>
    private async Task<string?> WithAttachmentBytesAsync(
        string? incoming, string testerId, Guid clientId, Guid? supersedesClientId, CancellationToken ct)
    {
        if (!PulsationPayload.IsServerPointer(incoming, out var source)) return incoming;
        var candidates = new List<Guid> { source ?? clientId, clientId };
        if (supersedesClientId is { } previous) candidates.Add(previous);
        foreach (var id in candidates.Distinct())
        {
            var stored = await _db.MachineTests
                .Where(t => t.TesterId == testerId && t.ClientId == id)
                .Select(t => t.PayloadJson)
                .FirstOrDefaultAsync(ct);
            if (PulsationPayload.Base64(stored) is { } bytes) return PulsationPayload.WithBytes(incoming!, bytes);
        }
        return incoming;
    }

    /// <summary>The tester's Testing Company. There is no company claim on either auth scheme, so
    /// this is always a lookup — deliberately, since a claim would stay stale for the lifetime of
    /// the cookie/token after a transfer or deactivation.</summary>
    private Task<Guid?> CompanyOfAsync(string testerId, CancellationToken ct) =>
        _db.Users.Where(u => u.Id == testerId)
            .Select(u => u.TestingCompanyId).FirstOrDefaultAsync(ct);

    /// <summary>Plenty for any real plant — a guard on what a Device can push into the JSON column.</summary>
    private const int MaxPumpRows = 20;
    private const int MaxPumpFieldLength = 150;

    private static string? TrimField(string? s)
    {
        var t = s?.Trim();
        if (string.IsNullOrEmpty(t)) return null;
        return t.Length <= MaxPumpFieldLength ? t : t[..MaxPumpFieldLength];
    }

    private static VacuumPumpDetail CleanPump(VacuumPumpDetail p) =>
        new(TrimField(p.Make), TrimField(p.Model), TrimField(p.MotorSize), p.DrivesMilkPump, TrimField(p.RegulatorType));

    private static ReleaserPumpDetail CleanPump(ReleaserPumpDetail p) =>
        new(TrimField(p.Make), TrimField(p.Model), TrimField(p.MotorSize));

    /// <summary>A quantity is a count of regulators: nothing below one, nothing absurd.</summary>
    private static RegulatorDetail CleanRegulator(RegulatorDetail r) =>
        new(TrimField(r.Type), r.Quantity is > 0 ? Math.Min(r.Quantity.Value, 99) : null);

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
        if (dto.VacuumPumps is not null) cfg.VacuumPumps = dto.VacuumPumps.Take(MaxPumpRows).Select(CleanPump).ToList();
        if (dto.ReleaserPumps is not null) cfg.ReleaserPumps = dto.ReleaserPumps.Take(MaxPumpRows).Select(CleanPump).ToList();
        if (dto.Regulators is not null)
        {
            cfg.Regulators = dto.Regulators.Take(MaxPumpRows).Select(CleanRegulator)
                .Where(r => r.Type is not null || r.Quantity is not null).ToList();
            cfg.RegulatorsSuitable = dto.RegulatorsSuitable;
        }
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
        c.HasTeatSprayer, c.HasBackingGate, c.HasReleaserPump,
        c.VacuumPumps, c.ReleaserPumps, c.Regulators, c.RegulatorsSuitable);
}
