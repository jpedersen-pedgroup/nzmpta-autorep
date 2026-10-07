using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Autorep.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace Autorep.Web.Api;

// The tester's farm book. Testers pick farms on the device, from a copy of this list cached in
// their IndexedDB (Client/sync/farmsSync.ts) — so starting a test works with no signal — and the
// wizard snapshots the chosen farm's details for offline display. Farm EDITING stays in the admin
// portal; the only write here is a tester adding a new farm from the New-test page, which is
// online-only by decision (plans/offline-tester-app.md, Phase 5 cut).
[ApiController]
[Route("api/farms")]
[Authorize]
public class FarmsController : ControllerBase
{
    private readonly AutorepDbContext _db;
    private readonly FarmReviewNotifier _reviewNotifier;

    public FarmsController(AutorepDbContext db, FarmReviewNotifier reviewNotifier)
    {
        _db = db;
        _reviewNotifier = reviewNotifier;
    }

    public record FarmDto(
        Guid Id, string Name, string? SupplyNumber, string? AddressLine1, string? AddressLine2,
        string? Town, string? PostCode, string? RapidNumber, string? RegionName, string? MilkCompanyName,
        string? FarmerName, string? ContactPhone, string? ContactEmail, Guid? MilkCompanyId = null);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // List: the caller's farm book, cached on-device so the New-test picker and the wizard work
    // offline (including farms the office added since the last visit). Same visibility rule as
    // everywhere else — the shared FarmScope predicate — never the whole national list (farm PII
    // scoping).
    //
    // Every tester page refreshes it, and it is the largest thing they fetch, so it carries an
    // ETag over the exact bytes: the device sends back the one it holds and gets a bodyless 304
    // until something in its book changes — a farm edited, added, deactivated or brought into
    // scope. The body stays a plain array, so a device still running an older bundle keeps working.
    // no-store, so the browser's own HTTP cache never keeps a copy of farmers' contact details on
    // a shared device: the conditional request is the device's to make, from IndexedDB.
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var query = _db.Farms
            .Include(x => x.Region)
            .Include(x => x.MilkSupplyCompany)
            .Where(x => x.IsActive);

        if (!User.IsInRole(Roles.SuperAdministrator))
            query = query.InCompanyScope(_db, await CompanyIdAsync(ct), TesterId());

        var farms = await query
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .Select(f => new FarmDto(
                f.Id, f.Name, f.SupplyNumber, f.AddressLine1, f.AddressLine2,
                f.Town, f.PostCode, f.RapidNumber,
                f.Region == null ? null : f.Region.Name,
                f.MilkSupplyCompany == null ? null : f.MilkSupplyCompany.Name,
                f.FarmerName, f.ContactPhone, f.ContactEmail, f.MilkSupplyCompanyId))
            .ToListAsync(ct);

        var body = JsonSerializer.SerializeToUtf8Bytes(farms, Json);
        var etag = new EntityTagHeaderValue($"\"{Convert.ToHexString(SHA256.HashData(body))[..32]}\"");
        Response.Headers.CacheControl = "no-store";
        Response.Headers.ETag = etag.ToString();
        if (Request.GetTypedHeaders().IfNoneMatch.Any(t => t.Compare(etag, useStrongComparison: true)))
            return StatusCode(StatusCodes.Status304NotModified);

        return File(body, "application/json; charset=utf-8");
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        // Scope to farms the caller has a legitimate relationship with, so a tester can't harvest
        // every farmer's contact details by iterating ids. Super-Admins see any farm; everyone
        // else only farms in their company's scope (set up or tested by the company — the shared
        // FarmScope predicate). Return NotFound for out-of-scope ids so their existence isn't
        // disclosed.
        var query = _db.Farms
            .Include(x => x.Region)
            .Include(x => x.MilkSupplyCompany)
            .Where(x => x.Id == id);

        if (!User.IsInRole(Roles.SuperAdministrator))
            query = query.InCompanyScope(_db, await CompanyIdAsync(ct), TesterId());

        var f = await query.FirstOrDefaultAsync(ct);

        if (f is null) return NotFound();

        return Ok(ToDto(f));
    }

    public record RegionOption(Guid Id, string Name, string Island);
    public record MilkCompanyOption(Guid Id, string Name);
    public record NewFarmOptions(IReadOnlyList<RegionOption> Regions, IReadOnlyList<MilkCompanyOption> MilkCompanies);

    // The add-a-farm form's two pick lists. Fetched when the form opens — it only works online, so
    // the device never needs its own copy. Reference data, not PII.
    [HttpGet("new-farm-options")]
    [Authorize(Roles = Roles.Tester)]
    public async Task<IActionResult> NewFarmOptionsList(CancellationToken ct)
    {
        var regions = await _db.Regions.Where(r => r.IsActive)
            .OrderBy(r => r.Island).ThenBy(r => r.SortOrder).ThenBy(r => r.Name)
            .Select(r => new RegionOption(r.Id, r.Name, r.Island))
            .ToListAsync(ct);
        var companies = await _db.MilkSupplyCompanies.Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new MilkCompanyOption(c.Id, c.Name))
            .ToListAsync(ct);
        return Ok(new NewFarmOptions(regions, companies));
    }

    public record NewFarmRequest(
        string? Name, string? SupplyNumber, Guid? MilkSupplyCompanyId, Guid? RegionId,
        string? AddressLine1, string? AddressLine2, string? Town, string? PostCode, string? RapidNumber,
        string? FarmerName, string? ContactPhone, string? ContactEmail);

    // A tester adds a farm from the New-test page. Was a Razor handler on that page, posting with an
    // antiforgery token borrowed from the layout's sign-out form; the page is now drawn by the
    // bundle (the same page the offline shell shows) and has no token, and API controllers take a
    // JSON body without one — the same footing as the sync push. Online-only by decision: the device
    // never creates farms of its own.
    [HttpPost]
    [Authorize(Roles = Roles.Tester)]
    public async Task<IActionResult> Create([FromBody] NewFarmRequest farm, CancellationToken ct)
    {
        // A lapsed licence can't start tests, so it has no reason to add farms.
        if (User.HasClaim(LicenceScope.ScopeClaim, LicenceScope.SyncOnly)) return Forbid();

        var name = farm.Name?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { error = "Farm name is required." });

        // A tester without a Testing Company can't own the new farm, so it would fall outside
        // every scope check the moment it was created — refuse up front rather than strand an
        // orphan farm row they could never pick.
        var companyId = await CompanyIdAsync(ct);
        if (companyId is null)
            return BadRequest(new { error = "Your account isn't linked to a Testing Company, so it can't add farms. Ask your administrator." });

        // The form only offers active rows; anything else is a stale form or a crafted request,
        // and would otherwise surface as a foreign-key failure.
        if (farm.RegionId is { } regionId && !await _db.Regions.AnyAsync(r => r.Id == regionId && r.IsActive, ct))
            return BadRequest(new { error = "That region isn't available any more. Choose another." });
        if (farm.MilkSupplyCompanyId is { } milkId && !await _db.MilkSupplyCompanies.AnyAsync(c => c.Id == milkId && c.IsActive, ct))
            return BadRequest(new { error = "That milk supply company isn't available any more. Choose another." });

        var entity = new Farm
        {
            Name = name,
            // Tag the creating company so it appears in this company's farm book straight away,
            // even before the first test is synced against it.
            CreatedByTestingCompanyId = companyId,
            CreatedByTesterId = TesterId(),
            // A plain Tester's field-created farm goes under review by a Company Administrator;
            // an administrator adding a farm needs no second pair of eyes. Either way the farm
            // is usable for testing immediately.
            PendingReviewSince = IsAdministrator() ? null : DateTimeOffset.UtcNow,
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
        await _db.SaveChangesAsync(ct);

        // Not tied to the request's cancellation: the farm is saved, so the administrators must hear
        // about it even if the tester's connection drops before the answer gets back to them.
        if (entity.PendingReviewSince is not null)
            await _reviewNotifier.NotifyPendingFarmAsync(entity, $"{Request.Scheme}://{Request.Host}/Admin/Farms/Edit/{entity.Id}");

        // The full row, as the farm book carries it, so the device can add it to its cached book
        // and offer it offline from now on without waiting for the next sync.
        var created = await _db.Farms
            .Include(x => x.Region)
            .Include(x => x.MilkSupplyCompany)
            .FirstAsync(x => x.Id == entity.Id, ct);
        return Ok(ToDto(created));
    }

    private static FarmDto ToDto(Farm f) => new(
        f.Id, f.Name, f.SupplyNumber, f.AddressLine1, f.AddressLine2,
        f.Town, f.PostCode, f.RapidNumber, f.Region?.Name, f.MilkSupplyCompany?.Name,
        f.FarmerName, f.ContactPhone, f.ContactEmail, f.MilkSupplyCompanyId);

    private string? TesterId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

    // A user can hold Tester alongside an administrator role; administrators' farms skip review.
    private bool IsAdministrator() =>
        User.IsInRole(Roles.CompanyAdministrator) || User.IsInRole(Roles.SuperAdministrator);

    /// <summary>The caller's Testing Company — always a lookup, never a claim, so a transfer or
    /// deactivation takes effect on the next request rather than the next sign-in.</summary>
    private Task<Guid?> CompanyIdAsync(CancellationToken ct)
    {
        var testerId = TesterId();
        return _db.Users.Where(u => u.Id == testerId).Select(u => u.TestingCompanyId).FirstOrDefaultAsync(ct);
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
