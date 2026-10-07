using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Autorep.Web.Services;
using Autorep.Web.Services.Pdfs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Api;

// Tester sync surface. Tests are created/edited on-device (IndexedDB) and pushed here,
// upserting by ClientId so retries are safe; the list endpoint lets a Device pull the
// Tester's tests back (new device, or to refresh) — including versions an administrator made
// of them, which carry the tester's id. A version that collides with another version of the
// same test is reconciled through tests/merge (see Services/Reconciliation).
[ApiController]
[Route("api/sync")]
[Authorize(Roles = Roles.Tester)]
public class SyncController : ControllerBase
{
    private readonly AutorepDbContext _db;
    private readonly FarmReviewNotifier _reviewNotifier;
    private readonly Reconciliation _reconciliation;
    private readonly PulsationAttachments _attachments;
    private readonly ILogger<SyncController> _log;

    public SyncController(
        AutorepDbContext db, FarmReviewNotifier reviewNotifier, Reconciliation reconciliation,
        PulsationAttachments attachments, ILogger<SyncController> log)
    {
        _db = db;
        _reviewNotifier = reviewNotifier;
        _reconciliation = reconciliation;
        _attachments = attachments;
        _log = log;
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
        DateOnly? NextTestDate = null,
        // The second parent of an automatic merge (see MergeTests), when a device re-sends one.
        Guid? MergedFromClientId = null);

    /// <summary>A test as the pull delivers it. A soft-deleted one comes as a TOMBSTONE —
    /// <c>Deleted</c> set, with when and why — so the device removes its copy (or, if it holds edits
    /// the server hasn't seen, keeps and flags it). The payload still comes too: a device older than
    /// tombstones would otherwise overwrite its copy with an empty shell.</summary>
    public record TestSummaryDto(
        Guid ClientId, string FarmName, DateTimeOffset CreatedAt,
        DateTimeOffset? MarkedCompleteAt, ConfigDto? Config, string? PayloadJson,
        bool Deleted = false, DateTimeOffset? DeletedAt = null, string? DeletedReason = null);

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
    // offset over a total order (created, then id), which is only safe while rows never LEAVE the
    // result set: a row leaving it ahead of the cursor would shift the rest up and the next page
    // would skip one. So a soft-deleted test is never filtered out here — it stays, as a tombstone
    // (TestSummaryDto.Deleted) — and a row's UpdatedAt only ever moves forward, so nothing leaves the
    // since-window either. Rows can only ARRIVE (a new version at the top, or an old row whose
    // UpdatedAt moved, wherever it sorts), which shifts the rest down: a row re-delivered, never one
    // skipped, and the pull upserts. Without ?limit= the answer is the whole set, exactly as a device
    // that predates paging expects.
    //
    // ?attachments=omit leaves the pulsation analyser PDFs' bytes on the server (marking each
    // attachment as held there): the device fetches one back from tests/{clientId}/pulsation-pdf
    // when it prints, rather than storing every PDF the tester ever attached. Without it, each PDF
    // is read back from the PDF store and put inline, as every pull did before the store — so an
    // older device keeps working.
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
        var dtos = new List<TestSummaryDto>(tests.Count);
        foreach (var t in tests)
        {
            dtos.Add(new TestSummaryDto(
                t.ClientId!.Value,
                t.Farm?.Name ?? string.Empty,
                t.CreatedAt,
                t.MarkedCompleteAt,
                t.Configuration is null ? null : ToDto(t.Configuration),
                omitAttachments
                    ? PulsationPayload.WithoutBytes(t.PayloadJson)
                    : await _attachments.RehydrateAsync(t.PayloadJson, t.TesterId, t.ClientId!.Value, ct),
                t.IsDeleted,
                t.DeletedAt,
                t.DeletedReason));
        }

        return Ok(new PullResponse(watermark, dtos, next));
    }

    // The pulsation analyser PDF attached to one of the tester's own tests, by the device's id for
    // it — for a device that has dropped its copy (or never pulled the bytes) and is printing.
    [HttpGet("tests/{clientId:guid}/pulsation-pdf")]
    public async Task<IActionResult> GetPulsationPdf(Guid clientId, CancellationToken ct)
    {
        var testerId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("No NameIdentifier claim on principal.");
        var payload = await _db.MachineTests
            .Where(t => t.TesterId == testerId && t.ClientId == clientId && !t.IsDeleted)
            .Select(t => t.PayloadJson)
            .FirstOrDefaultAsync(ct);

        byte[]? bytes;
        try
        {
            bytes = await _attachments.BytesAsync(testerId, clientId, payload, ct);
        }
        catch (PdfStoreException e)
        {
            _log.LogError(e, "Pulsation PDF for client {ClientId} could not be read from the PDF store", clientId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "The PDF store is unavailable right now." });
        }
        if (bytes is null) return NotFound();
        Response.Headers.CacheControl = "no-store";
        return File(bytes, "application/pdf", PulsationPayload.FileName(payload) ?? "pulsation-analyser.pdf");
    }

    /// <summary>
    /// The answer to a push that arrived second: something else — an administrator's edit, or this
    /// tester's other device — had already replaced the version this one was made from. Nothing has
    /// been stored. The device combines its version with <c>Head</c> (both made from <c>Base</c>) and
    /// sends the pair to <c>tests/merge</c>. Payloads come without the analyser PDF's bytes.
    /// </summary>
    public record CollisionResponse(string Conflict, Guid BaseClientId, TestSummaryDto Base, TestSummaryDto Head, int HeadVersion);

    /// <summary>A tester's version that collided, and the device's combine of it with the head.</summary>
    public record MergeRequest(UploadTestRequest Incoming, UploadTestRequest Merged, Guid HeadClientId);

    // Push: upsert by ClientId (idempotent), linking the Farm by id / farm identity within the
    // tester's company scope (see ResolveFarmAsync), creating a company-tagged farm if needed.
    //
    // A version already signed off on the server is a record and never changes in place (see
    // FrozenAsync). A version (one that replaces an earlier version) is then checked for a
    // collision: if something else has already replaced its parent — an administrator edited the test
    // while this device was offline — a SIGNED-OFF version is answered 409 with what the device needs
    // to combine the two (see Reconciliation). An in-progress one is stored as usual: it's still a
    // draft, and is combined when it's signed off. Either way the collision is recorded.
    [HttpPost("tests")]
    public async Task<IActionResult> UploadTest([FromBody] UploadTestRequest req, CancellationToken ct)
    {
        var testerId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("No NameIdentifier claim on principal.");

        if (string.IsNullOrWhiteSpace(req.FarmName))
            return BadRequest(new { error = "FarmName is required" });

        // Signed off already: unchanged, or refused — deleted or not (the pull's tombstone tells the
        // device about a deletion).
        if (await FrozenAsync(testerId, req, ct) is { } frozen) return frozen;

        // A test NZMPTA has soft-deleted stays deleted. What the device sends is still kept with it —
        // it may be the only copy of the tester's latest edits — and the answer says so, so the device
        // can tell the tester. There's nothing to reconcile with.
        var deleted = await DeletedLineageAsync(testerId, req.ClientId, req.SupersedesClientId, ct);

        if (deleted is null
            && req.SupersedesClientId is { } parent
            && await _reconciliation.CollisionAsync(testerId, req.ClientId, parent, ct) is { } collision)
        {
            await _reconciliation.NotePendingAsync(collision.Base, collision.Head, req.ClientId, SyncConflictSource.Push, ct);
            if (req.MarkedCompleteAt is not null)
            {
                await _db.SaveChangesAsync(ct);
                return Conflict(await CollisionBodyAsync(collision, ct));
            }
        }

        Stored stored;
        try
        {
            stored = await StoreAsync(req, testerId, ct);
            // Deleted between that check and the parent being read for this write: it still joins the
            // deleted test. (A deletion committing after the read makes this save fail instead: the
            // parent's stamp has moved.)
            deleted ??= stored.Parent is { IsDeleted: true } deletedParent ? deletedParent : null;
            if (deleted is not null)
            {
                // Versions that arrived before this one (a device sends in whatever order its store
                // lists them) and have just taken its root join the deletion with it.
                var now = DateTimeOffset.UtcNow;
                foreach (var row in stored.Adopted.Prepend(stored.Test).Where(r => !r.IsDeleted))
                {
                    row.IsDeleted = true;
                    row.DeletedAt = deleted.DeletedAt;
                    row.DeletedById = deleted.DeletedById;
                    row.DeletedReason = deleted.DeletedReason;
                    row.UpdatedAt = now;
                }
            }
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another writer replaced this test's parent (or this version) in the same moment. Nothing
            // was stored; the device retries on its next sync and gets a straight answer then.
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "This test changed on the server at the same moment — it will be sent again." });
        }

        if (stored.NewFarm is { PendingReviewSince: not null } farm)
            await _reviewNotifier.NotifyPendingFarmAsync(farm,
                $"{Request.Scheme}://{Request.Host}/Admin/Farms/Edit/{farm.Id}", ct);

        if (deleted is not null)
            return Ok(new { id = stored.Test.Id, status = "deleted", deletedAt = deleted.DeletedAt, reason = deleted.DeletedReason });

        return stored.Created
            ? CreatedAtAction(nameof(GetTest), new { id = stored.Test.Id }, new { id = stored.Test.Id, status = "created" })
            : Ok(new { id = stored.Test.Id, status = "updated" });
    }

    /// <summary>
    /// The tester's device combined its signed-off version (<c>Incoming</c>) with the test's current
    /// version (<c>HeadClientId</c>) after a 409 from the push. Stores both: the incoming version as
    /// its own version (both states are kept) and the combined one, which replaces the head and names
    /// the incoming version as merged in. If the test has moved on again since the 409, the answer is
    /// another 409 with the new head and the device combines again. Sending the same merge twice is
    /// harmless: the second is answered "already-merged" and changes nothing.
    /// </summary>
    [HttpPost("tests/merge")]
    public async Task<IActionResult> MergeTests([FromBody] MergeRequest req, CancellationToken ct)
    {
        var testerId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("No NameIdentifier claim on principal.");
        var incoming = req.Incoming;
        var merged = req.Merged;

        if (incoming.SupersedesClientId is not { } baseClientId
            || merged.SupersedesClientId != req.HeadClientId
            || merged.MergedFromClientId != incoming.ClientId
            || merged.ClientId == incoming.ClientId
            || incoming.MarkedCompleteAt is null || merged.MarkedCompleteAt is null
            || string.IsNullOrWhiteSpace(incoming.FarmName) || string.IsNullOrWhiteSpace(merged.FarmName))
            return BadRequest(new { error = "That isn't a merge of two signed-off versions of one test." });

        var done = await _db.MachineTests
            .Where(t => t.TesterId == testerId && t.MergedFromClientId == incoming.ClientId)
            .Select(t => t.ClientId)
            .FirstOrDefaultAsync(ct);
        if (done is not null) return Ok(new { status = "already-merged", mergedClientId = done });

        var baseVersion = await _db.MachineTests
            .FirstOrDefaultAsync(t => t.TesterId == testerId && t.ClientId == baseClientId, ct);
        if (baseVersion is null) return NotFound(new { error = "The version these were made from isn't on the server." });
        // Deleted since the 409: nothing to combine with. The device's next push stores its version
        // with the deleted test (see UploadTest).
        if (baseVersion.IsDeleted) return StatusCode(StatusCodes.Status410Gone, new { error = "deleted" });

        var versions = await TestLineage.VersionsAsync(_db, baseVersion, ct);
        var head = TestLineage.Head(versions, excludingClientId: incoming.ClientId);
        if (head is null) return BadRequest(new { error = "This test has no signed-off version to combine with." });
        if (head.ClientId != req.HeadClientId)
            return Conflict(await CollisionBodyAsync(new Reconciliation.Collision(baseVersion, head), ct));
        if (merged.Version is not { } mergedVersion || mergedVersion <= Math.Max(head.Version, incoming.Version ?? 1))
            return BadRequest(new { error = "A combined version must be numbered after both versions it combines." });
        if (await _db.MachineTests.AnyAsync(t => t.TesterId == testerId && t.ClientId == merged.ClientId, ct))
            return BadRequest(new { error = "The combined version's id is already in use." });

        // The combine must follow the reconciliation rule, field by field — checked here against the
        // stored versions, never taken on trust. A defective or tampered client must not drop the
        // head's changes, or slip in changes nobody made, behind a version every list then treats as
        // current. (The device does the combine because the calculated readings and the worded record
        // come from its TypeScript; the outcome of the rule is the server's to hold it to.)
        var mergedJson = PayloadUnits.Parse(merged.PayloadJson);
        if (mergedJson is null) return BadRequest(new { error = "The combined version didn't arrive." });
        var headJson = PayloadUnits.Parse(head.PayloadJson);
        var incomingJson = PayloadUnits.Parse(incoming.PayloadJson);
        var departures = PayloadUnits.MergeDepartures(PayloadUnits.Parse(baseVersion.PayloadJson), headJson, incomingJson, mergedJson)
            .ToList();
        if (!KeepsEvery(headJson?["attestations"], mergedJson["attestations"])
            || !KeepsEvery(incomingJson?["attestations"], mergedJson["attestations"]))
            departures.Add("attestations");
        var headHistory = headJson?["amendments"] as JsonArray ?? [];
        if (mergedJson["amendments"] is not JsonArray history
            || history.Count != headHistory.Count + 1
            || headHistory.Where((earlier, i) => !PayloadUnits.Same(earlier, history[i])).Any())
            departures.Add("amendments");
        if (departures.Count > 0)
            return UnprocessableEntity(new { error = "not-the-merge", fields = departures });

        // Its own bookkeeping is the server's, whatever was sent.
        mergedJson["id"] = merged.ClientId.ToString();
        mergedJson["version"] = mergedVersion;
        mergedJson["supersedesId"] = head.ClientId!.Value.ToString();
        mergedJson["mergedFromId"] = incoming.ClientId.ToString();
        // As complete as the later of the two sign-offs it combines (a merge isn't a sign-off).
        var completedAt = head.MarkedCompleteAt > incoming.MarkedCompleteAt ? head.MarkedCompleteAt : incoming.MarkedCompleteAt;

        try
        {
            // The incoming version, as its own version — then the combine, which may take its PDF.
            // Signed off here already (a retry whose answer was lost): it stands as stored.
            var existingIncoming = await _db.MachineTests
                .FirstOrDefaultAsync(t => t.TesterId == testerId && t.ClientId == incoming.ClientId, ct);
            MachineTest incomingRow;
            if (existingIncoming?.MarkedCompleteAt is not null)
            {
                if (PayloadUnits.Changed(PayloadUnits.Parse(existingIncoming.PayloadJson), incomingJson).Count > 0)
                    return Conflict(new { error = "completed" });
                incomingRow = existingIncoming;
            }
            else
            {
                incomingRow = (await StoreAsync(incoming, testerId, ct)).Test;
            }
            // The combine's analyser PDF is one of the two versions' — it points at that version's
            // stored copy (the incoming row is written in this same save, so it's passed in).
            var mergedPayload = await _attachments.StoreIncomingAsync(
                mergedJson.ToJsonString(PayloadWrite), testerId, merged.ClientId, head.ClientId, ct,
                unsaved: new Dictionary<Guid, string?> { [incoming.ClientId] = incomingRow.PayloadJson });

            var combined = new MachineTest
            {
                ClientId = merged.ClientId,
                TesterId = testerId,
                TestingCompanyId = head.TestingCompanyId,
                FarmId = head.FarmId,
                RootClientId = TestLineage.KeyOf(head),
                // Mirrored from the checked payload, not from the request's own fields.
                Notes = mergedJson["notes"] is JsonValue notes && notes.TryGetValue<string>(out var text) ? text : null,
                MarkedCompleteAt = completedAt,
                CreatedAt = DateTimeOffset.UtcNow,
                PayloadJson = mergedPayload,
                Version = mergedVersion,
                SupersedesClientId = head.ClientId,
                MergedFromClientId = incoming.ClientId,
                NextTestDate = mergedJson["nextTestDate"] is JsonValue next && next.TryGetValue<string>(out var day)
                    && DateOnly.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var due)
                        ? due
                        : head.NextTestDate,
            };
            // The head's configuration columns carry over, then the checked payload's configuration.
            if (await _db.MachineConfigurations.FirstOrDefaultAsync(c => c.MachineTestId == head.Id, ct) is { } headConfig)
                ApplyConfig(combined, ToDto(headConfig));
            if (mergedJson["config"] is JsonObject config)
            {
                try
                {
                    ApplyConfig(combined, config.Deserialize<ConfigDto>(JsonSerializerOptions.Web));
                }
                catch (JsonException)
                {
                    return BadRequest(new { error = "The combined version's machine configuration couldn't be read." });
                }
            }
            _db.MachineTests.Add(combined);
            head.SuccessorStamp = Guid.NewGuid();

            await _reconciliation.NoteMergedAsync(baseVersion, head, incomingRow, combined,
                incomingRow.PayloadJson, ct);
            await _db.SaveChangesAsync(ct);
            return Ok(new { status = "merged", id = combined.Id, mergedClientId = combined.ClientId });
        }
        catch (DbUpdateConcurrencyException)
        {
            // The head was replaced while this was being stored: combine with the new one.
            _db.ChangeTracker.Clear();
            var again = await _db.MachineTests.FirstAsync(t => t.Id == baseVersion.Id, ct);
            var newHead = TestLineage.Head(await TestLineage.VersionsAsync(_db, again, ct), excludingClientId: incoming.ClientId);
            return newHead is null
                ? StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Try again in a moment." })
                : Conflict(await CollisionBodyAsync(new Reconciliation.Collision(again, newHead), ct));
        }
    }

    /// <summary>
    /// A version already signed off on the server is a record: it never changes in place, whoever
    /// pushes it — the tester's own device, or anyone holding its ClientId (an administrator's version
    /// carries the tester's id, so the tester's device knows it). Re-sending it unchanged is a harmless
    /// retry, answered "unchanged" without writing anything; a push that would change it (or undo its
    /// sign-off) is refused with a 409 naming the fields — a change belongs in a new version. Null when
    /// the push is to a draft or a version the server hasn't got.
    /// </summary>
    private async Task<IActionResult?> FrozenAsync(string testerId, UploadTestRequest req, CancellationToken ct)
    {
        var stored = await _db.MachineTests.AsNoTracking()
            .Where(t => t.TesterId == testerId && t.ClientId == req.ClientId && t.MarkedCompleteAt != null)
            .Select(t => new { t.Id, t.PayloadJson })
            .FirstOrDefaultAsync(ct);
        if (stored is null) return null;
        var changed = req.MarkedCompleteAt is null
            ? ["markedCompleteAt"]
            : PayloadUnits.Changed(PayloadUnits.Parse(stored.PayloadJson), PayloadUnits.Parse(req.PayloadJson)).ToList();
        return changed.Count == 0
            ? Ok(new { id = stored.Id, status = "unchanged" })
            : Conflict(new { error = "completed", fields = changed });
    }

    /// <summary>Whether every attestation in <paramref name="from"/> is also in <paramref name="to"/>.</summary>
    private static bool KeepsEvery(JsonNode? from, JsonNode? to)
    {
        var kept = (to as JsonArray)?.ToList() ?? [];
        return ((from as JsonArray)?.ToList() ?? []).All(a => kept.Any(k => PayloadUnits.Same(a, k)));
    }

    // Rewriting a payload must not re-encode its text (macrons in Māori place names): see PulsationPayload.
    private static readonly JsonSerializerOptions PayloadWrite = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private async Task<CollisionResponse> CollisionBodyAsync(Reconciliation.Collision collision, CancellationToken ct)
    {
        async Task<TestSummaryDto> SummaryOf(Guid id)
        {
            var t = await _db.MachineTests.AsNoTracking()
                .Include(x => x.Farm).Include(x => x.Configuration)
                .FirstAsync(x => x.Id == id, ct);
            return new TestSummaryDto(t.ClientId!.Value, t.Farm?.Name ?? string.Empty, t.CreatedAt, t.MarkedCompleteAt,
                t.Configuration is null ? null : ToDto(t.Configuration), PulsationPayload.WithoutBytes(t.PayloadJson));
        }
        return new CollisionResponse("superseded", collision.Base.ClientId!.Value,
            await SummaryOf(collision.Base.Id), await SummaryOf(collision.Head.Id), collision.Head.Version);
    }

    /// <summary>The deleted version a push belongs with, when its test has been soft-deleted: the row
    /// itself, the version it was made from, or any other version of the test it joins (by the root it
    /// takes, which is its parent's when the parent is here) — so a deletion further up the chain is
    /// found too, not only one in the parent. A version that arrives before its own parent can't be
    /// placed yet; it joins the deletion when the parent arrives (see UploadTest).</summary>
    private async Task<MachineTest?> DeletedLineageAsync(string testerId, Guid clientId, Guid? parentClientId, CancellationToken ct)
    {
        var root = await TestLineage.RootForNewVersionAsync(_db, testerId, clientId, parentClientId, ct);
        return await _db.MachineTests.AsNoTracking()
            .Where(t => t.TesterId == testerId && t.IsDeleted
                && (t.ClientId == clientId
                    || (parentClientId != null && t.ClientId == parentClientId)
                    || t.ClientId == root
                    || t.RootClientId == root))
            .OrderBy(t => t.DeletedAt)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>What a push stored: the row, whether it was new, a farm it had to create, the version it
    /// was made from (as read for this write), and versions that had arrived before it and now take
    /// its root.</summary>
    private sealed record Stored(MachineTest Test, bool Created, Farm? NewFarm, MachineTest? Parent, IReadOnlyList<MachineTest> Adopted);

    /// <summary>
    /// Upserts a pushed test (not yet saved). A new VERSION of a test already on the server stays
    /// with that test: the same farm and the same company as its parent, whatever company the tester
    /// is with now — an amendment is more work on the original test, not a new test for the tester's
    /// current employer. A version signed off here, or a new one started, renews its parent's
    /// <see cref="MachineTest.SuccessorStamp"/>, so a rival writer that read the same parent fails
    /// rather than forking the test — or, for a deletion, rather than missing the new version.
    /// </summary>
    private async Task<Stored> StoreAsync(UploadTestRequest req, string testerId, CancellationToken ct)
    {
        // Scope the upsert to the caller's own tests: a ClientId belonging to another tester must
        // never match here (otherwise tester A could overwrite tester B's test — IDOR). Combined
        // with the unique (TesterId, ClientId) index, a foreign ClientId falls through to create.
        var existing = await _db.MachineTests
            .Include(t => t.Configuration)
            .FirstOrDefaultAsync(t => t.ClientId == req.ClientId && t.TesterId == testerId, ct);

        // The analyser PDF goes to the PDF store and the payload keeps a pointer to it. A device that
        // dropped its copy re-sends the test with a pointer in its place: that is matched to the copy
        // already stored, so a re-push can never lose it (PulsationAttachments.StoreIncomingAsync).
        var payloadJson = await _attachments.StoreIncomingAsync(req.PayloadJson, testerId, req.ClientId, req.SupersedesClientId, ct);

        var parent = req.SupersedesClientId is { } parentId
            ? await _db.MachineTests.FirstOrDefaultAsync(t => t.TesterId == testerId && t.ClientId == parentId, ct)
            : null;
        if (parent is not null && (existing is null || (req.MarkedCompleteAt is not null && existing.MarkedCompleteAt is null)))
            parent.SuccessorStamp = Guid.NewGuid();

        if (existing is not null)
        {
            existing.Notes = req.Notes;
            existing.MarkedCompleteAt = req.MarkedCompleteAt;
            existing.PayloadJson = payloadJson;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            existing.Version = req.Version ?? existing.Version;
            existing.SupersedesClientId = req.SupersedesClientId ?? existing.SupersedesClientId;
            existing.MergedFromClientId = req.MergedFromClientId ?? existing.MergedFromClientId;
            existing.NextTestDate = req.NextTestDate ?? existing.NextTestDate;
            // TestingCompanyId is deliberately NOT re-stamped: it records the company the work was
            // done for. Re-deriving it here would drag a tester's old tests into their new company
            // the first time they re-synced after a transfer.
            ApplyConfig(existing, req.Config);
            return new Stored(existing, false, null, parent, []);
        }

        Farm? newFarm = null;
        Guid farmId;
        Guid? companyId;
        if (parent is not null)
        {
            farmId = parent.FarmId;
            companyId = parent.TestingCompanyId;
        }
        else
        {
            companyId = await CompanyOfAsync(testerId, ct);
            var farm = await ResolveFarmAsync(req, testerId, companyId, ct);
            // Whether ResolveFarmAsync minted a new farm row (vs linking an existing one) — checked
            // before SaveChanges flips the state, so the review notification fires exactly once.
            if (_db.Entry(farm).State == EntityState.Added) newFarm = farm;
            farmId = farm.Id;
        }

        var root = await TestLineage.RootForNewVersionAsync(_db, testerId, req.ClientId, req.SupersedesClientId, ct);
        var adopted = await TestLineage.AdoptDescendantsAsync(_db, testerId, req.ClientId, root, ct);

        var test = new MachineTest
        {
            ClientId = req.ClientId,
            TesterId = testerId,
            TestingCompanyId = companyId,
            FarmId = farmId,
            Farm = newFarm,
            RootClientId = root,
            Notes = req.Notes,
            MarkedCompleteAt = req.MarkedCompleteAt,
            CreatedAt = req.CreatedAt ?? DateTimeOffset.UtcNow,
            PayloadJson = payloadJson,
            Version = req.Version ?? 1,
            SupersedesClientId = req.SupersedesClientId,
            MergedFromClientId = req.MergedFromClientId,
            NextTestDate = req.NextTestDate,
        };
        ApplyConfig(test, req.Config);
        _db.MachineTests.Add(test);
        return new Stored(test, true, newFarm, parent, adopted);
    }

    [HttpGet("tests/{id:guid}")]
    public async Task<IActionResult> GetTest(Guid id, CancellationToken ct)
    {
        var testerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var test = await _db.MachineTests
            .Include(t => t.Farm)
            .Include(t => t.Configuration)
            .FirstOrDefaultAsync(t => t.Id == id && t.TesterId == testerId && !t.IsDeleted, ct);
        if (test is null) return NotFound();

        // Project to the DTO rather than returning the raw entity (avoids leaking the Farm
        // navigation and any future entity members through the sync surface). The analyser PDF goes
        // inline, as it always has here.
        return Ok(new TestSummaryDto(
            test.ClientId ?? Guid.Empty,
            test.Farm?.Name ?? string.Empty,
            test.CreatedAt,
            test.MarkedCompleteAt,
            test.Configuration is null ? null : ToDto(test.Configuration),
            await _attachments.RehydrateAsync(test.PayloadJson, test.TesterId, PulsationAttachments.ClientIdOf(test), ct)));
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

    internal static void ApplyConfig(MachineTest test, ConfigDto? dto)
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
