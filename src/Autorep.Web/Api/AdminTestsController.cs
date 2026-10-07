using System.Text.Json;
using System.Text.Json.Nodes;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Autorep.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Api;

// The admin portal's write path for Machine Tests. Online-only (the admin viewer is never served from
// the offline shell). Read access stays on /api/tests/{id}; this controller only ever ADDS versions —
// a stored version is never changed in place.
//
// Scope is applied to the query, as on the read side: a Company Administrator's out-of-company id
// doesn't match and reads as NotFound, never Forbid, so a test's existence isn't disclosed.
[ApiController]
[Route("api/admin/tests")]
[Authorize(Roles = Roles.SuperAdministrator + "," + Roles.CompanyAdministrator)]
public class AdminTestsController(
    AutorepDbContext db, AdminVersioning versioning, TestDeletion deletion, FinalReportStore reports,
    UserManager<Tester> users)
    : ControllerBase
{
    public record ReasonRequest(string? Reason);

    /// <summary>
    /// Soft-deletes the test version <paramref name="id"/> belongs to — every version of it — with a
    /// required reason (PRD story 70). Super-Administrator only. It leaves every list and the
    /// tester's device; the rows stay on record and can be restored.
    /// </summary>
    [HttpPost("{id:guid}/delete")]
    [Authorize(Roles = Roles.SuperAdministrator)]
    public async Task<IActionResult> Delete(Guid id, [FromBody] ReasonRequest req, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var me = await users.GetUserAsync(User);
        if (me is null) return Forbid();
        return Answer(await deletion.DeleteAsync(id, req.Reason, me.Id, ct));
    }

    /// <summary>Undoes a soft-delete: every version of the test comes back, and returns to the
    /// tester's device on its next sync. Super-Administrator only.</summary>
    [HttpPost("{id:guid}/restore")]
    [Authorize(Roles = Roles.SuperAdministrator)]
    public async Task<IActionResult> Restore(Guid id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var me = await users.GetUserAsync(User);
        if (me is null) return Forbid();
        return Answer(await deletion.RestoreAsync(id, me.Id, ct));
    }

    private IActionResult Answer(TestDeletion.Result result) => result switch
    {
        TestDeletion.Done done => Ok(new { versions = done.Versions.Count }),
        TestDeletion.Invalid invalid => BadRequest(new { error = "invalid", message = invalid.Message }),
        TestDeletion.Conflict conflict => Conflict(new { error = conflict.Error }),
        _ => NotFound(),
    };
    /// <summary>The edited test (the browser's LocalTest, with this edit's amendment record appended
    /// to its history) and why it was changed.</summary>
    public record SaveVersionRequest(string? PayloadJson, string? Reason);

    public record SavedVersionDto(Guid Id, Guid ClientId, int Version);

    /// <summary>A test's audit trail for the admin viewer's panel (PRD stories 68–69).</summary>
    public record HistoryDto(
        IReadOnlyList<HistoryVersionDto> Versions,
        IReadOnlyList<HistoryConflictDto> Conflicts,
        IReadOnlyList<HistoryEventDto> Events);

    /// <summary>One version: who made it (the tester, an administrator, or an automatic merge), its
    /// own amendment record (what it changed, field by field, in words — null on an original) and the
    /// attestations it carries (which checklist sections were bulk-confirmed).</summary>
    public record HistoryVersionDto(
        Guid Id, int Version, Guid? ClientId, Guid? SupersedesClientId, Guid? MergedFromClientId,
        DateTimeOffset CreatedAt, DateTimeOffset? MarkedCompleteAt,
        string? Author, string AuthorKind, bool IsCurrent, bool IsDeleted,
        JsonNode? Amendment, JsonNode? Attestations);

    /// <summary>A collision of two versions (SyncConflict). The versions are named by id — the two that
    /// collided were both made from the same version, so they share a version number — and the
    /// overlapping fields as payload paths.</summary>
    public record HistoryConflictDto(
        Guid Id, string Status, string DetectedOn, DateTimeOffset DetectedAt, DateTimeOffset? ResolvedAt,
        Guid? BaseId, Guid? HeadId, Guid? IncomingId, Guid? MergedId,
        IReadOnlyList<string> OverlappingFields);

    /// <summary>An administrator's action on the test, from the audit log: the version it saved
    /// (AdminVersionCreated) and the reason given (a save or a deletion).</summary>
    public record HistoryEventDto(DateTimeOffset At, string? Actor, string Operation, int? Version, string? Reason);

    private static readonly string[] HistoryOperations = ["AdminVersionCreated", "SoftDeleted", "Restored"];

    /// <summary>
    /// The audit panel's data for the test version <paramref name="id"/> belongs to: every version of
    /// it, oldest first, the collisions between them, and administrators' edits, deletions and
    /// restores. Same scope as the read: a Company Administrator's out-of-company id is a 404.
    /// </summary>
    [HttpGet("{id:guid}/history")]
    public async Task<IActionResult> History(Guid id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var me = await users.GetUserAsync(User);
        if (me is null) return Forbid();
        var test = await db.MachineTests
            .AdministeredBy(User.IsInRole(Roles.SuperAdministrator), me.TestingCompanyId)
            .FirstOrDefaultAsync(t => t.Id == id, ct);
        if (test is null) return NotFound();

        var versions = (await TestLineage.VersionsAsync(db, test, ct))
            .OrderBy(v => v.Version).ThenBy(v => v.CreatedAt).ToList();
        var head = TestLineage.Head(versions);
        var people = versions.Select(v => v.AuthorId ?? v.TesterId).Distinct().ToList();
        var names = await db.Users.Where(u => people.Contains(u.Id))
            .Select(u => new { u.Id, Name = u.DisplayName != "" ? u.DisplayName : u.Email })
            .ToDictionaryAsync(u => u.Id, u => u.Name, ct);

        var versionDtos = versions.Select(v =>
        {
            var payload = PayloadUnits.Parse(v.PayloadJson);
            var own = (payload?["amendments"] as JsonArray)?.LastOrDefault() is JsonObject last
                && last["version"] is JsonValue n && n.TryGetValue<int>(out var number) && number == v.Version
                    ? last.DeepClone()
                    : null;
            var kind = v.AuthorId is not null ? "admin" : v.MergedFromClientId is not null ? "merge" : "tester";
            return new HistoryVersionDto(
                v.Id, v.Version, v.ClientId, v.SupersedesClientId, v.MergedFromClientId,
                v.CreatedAt, v.MarkedCompleteAt,
                names.GetValueOrDefault(v.AuthorId ?? v.TesterId), kind,
                head is not null && head.Id == v.Id, v.IsDeleted,
                own, payload?["attestations"]?.DeepClone());
        }).ToList();

        Guid? IdOf(Guid? clientId) => clientId is null ? null : versions.FirstOrDefault(v => v.ClientId == clientId)?.Id;
        var key = TestLineage.KeyOf(test);
        var conflicts = (await db.SyncConflicts
                .Where(c => c.TesterId == test.TesterId && c.RootClientId == key)
                .OrderBy(c => c.DetectedAt)
                .ToListAsync(ct))
            .Select(c => new HistoryConflictDto(
                c.Id, c.Status, c.DetectedOn, c.DetectedAt, c.ResolvedAt,
                IdOf(c.BaseClientId), IdOf(c.HeadClientId), IdOf(c.IncomingClientId), IdOf(c.MergedClientId),
                string.IsNullOrEmpty(c.OverlappingFieldsJson)
                    ? []
                    : JsonSerializer.Deserialize<List<string>>(c.OverlappingFieldsJson) ?? []))
            .ToList();

        var keys = versions.Select(v => v.Id.ToString()).ToList();
        var events = await db.AuditEntries
            .Where(e => e.EntityType == nameof(MachineTest) && keys.Contains(e.EntityKey) && HistoryOperations.Contains(e.Operation))
            .OrderBy(e => e.Timestamp)
            .Select(e => new { e.Timestamp, e.Actor, e.Operation, e.AfterJson })
            .ToListAsync(ct);
        var actors = events.Select(e => e.Actor).Distinct().ToList();
        var actorNames = await db.Users.Where(u => actors.Contains(u.Id))
            .Select(u => new { u.Id, Name = u.DisplayName != "" ? u.DisplayName : u.Email })
            .ToDictionaryAsync(u => u.Id, u => u.Name, ct);

        return Ok(new HistoryDto(
            versionDtos,
            conflicts,
            events.Select(e =>
            {
                var detail = PayloadUnits.Parse(e.AfterJson);
                return new HistoryEventDto(
                    e.Timestamp, actorNames.GetValueOrDefault(e.Actor ?? "") ?? e.Actor, e.Operation,
                    detail?["version"] is JsonValue v && v.TryGetValue<int>(out var n) ? n : null,
                    detail?["reason"] is JsonValue r && r.TryGetValue<string>(out var why) ? why : null);
            }).ToList()));
    }

    /// <summary>
    /// Saves an administrator's edit of the completed version <paramref name="id"/> as the test's next
    /// version. 201 with the new version; 404 outside the caller's scope; 409 when the version can't be
    /// edited (in progress, migrated) or a later version has replaced it (with that version, to open
    /// instead); 422 naming the fields the caller's role may not change; 400 without a reason.
    /// </summary>
    [HttpPost("{id:guid}/versions")]
    public async Task<IActionResult> SaveVersion(Guid id, [FromBody] SaveVersionRequest req, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var me = await users.GetUserAsync(User);
        if (me is null) return Forbid();

        var superAdmin = User.IsInRole(Roles.SuperAdministrator);
        var editor = new AdminVersioning.Editor(
            me.Id,
            me.UserName ?? me.Email ?? me.Id,
            string.IsNullOrWhiteSpace(me.DisplayName) ? me.UserName ?? me.Id : me.DisplayName,
            superAdmin);
        var scope = db.MachineTests.AdministeredBy(superAdmin, me.TestingCompanyId);

        return await versioning.SaveAsync(scope, id, req.PayloadJson, req.Reason, editor, ct) switch
        {
            AdminVersioning.Saved saved => Created($"/api/tests/{saved.Version.Id}",
                new SavedVersionDto(saved.Version.Id, saved.Version.ClientId!.Value, saved.Version.Version)),
            AdminVersioning.Stale stale => Conflict(new
            {
                error = AdminVersioning.Blocked.Superseded,
                latest = new { id = stale.Latest.Id, version = stale.Latest.Version, completedAt = stale.Latest.MarkedCompleteAt },
            }),
            AdminVersioning.Refused refused => Conflict(new { error = refused.Reason }),
            AdminVersioning.OutOfScope outOfScope => UnprocessableEntity(new { error = "fields-not-allowed", fields = outOfScope.Fields }),
            AdminVersioning.Invalid invalid => BadRequest(new { error = "invalid", message = invalid.Message }),
            _ => NotFound(),
        };
    }

    /// <summary>
    /// The Final Report of a version an administrator saved, as the admin viewer generated it right
    /// after the save — the tester's engine, the full report with the analyser's pages (PRD 73) — kept
    /// as that version's stored report (FinalReportStore), as a tester's device keeps the one it signed
    /// off. Only for a version made in the admin portal: a tester's version keeps the report its device
    /// sent at sign-off (409). The body is the PDF itself. Same scope as the save: 404 outside it.
    /// </summary>
    [HttpPut("{id:guid}/final-report")]
    [RequestSizeLimit(FinalReportStore.MaxBytes)]
    public async Task<IActionResult> PutFinalReport(Guid id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var me = await users.GetUserAsync(User);
        if (me is null) return Forbid();

        if (!FinalReportStore.IsPdf(Request.ContentType))
            return StatusCode(StatusCodes.Status415UnsupportedMediaType, new { error = "Send the report as application/pdf." });
        if (Request.ContentLength is > FinalReportStore.MaxBytes) return TooLarge();

        var version = await db.MachineTests
            .AdministeredBy(User.IsInRole(Roles.SuperAdministrator), me.TestingCompanyId)
            .Where(t => t.Id == id)
            .Select(t => new { t.Id, t.TesterId, t.ClientId, t.AuthorId })
            .FirstOrDefaultAsync(ct);
        if (version is null) return NotFound();
        if (version.AuthorId is null || version.ClientId is not { } clientId)
            return Conflict(new { error = "not-an-admin-version", message = "This version's report is the one its tester's device sent at sign-off." });

        byte[] bytes;
        try
        {
            bytes = await FinalReportStore.ReadBodyAsync(Request, ct);
        }
        catch (FinalReportStore.BodyTooLargeException)
        {
            return TooLarge();
        }

        return await reports.StoreAsync(version.Id, version.TesterId, clientId, bytes, me.Id, ct) switch
        {
            FinalReportStore.Stored { Status: "created" } s => StatusCode(StatusCodes.Status201Created,
                new FinalReportsController.StoredResponse(s.Status, s.Sha256, s.SizeBytes)),
            FinalReportStore.Stored s => Ok(new FinalReportsController.StoredResponse(s.Status, s.Sha256, s.SizeBytes)),
            FinalReportStore.NotPdf => BadRequest(new { error = "not-a-pdf", message = "That isn't a PDF." }),
            FinalReportStore.Busy => Conflict(new { error = "busy", message = "The report was being stored by another request — try again." }),
            _ => StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "unavailable", message = "The report store is unavailable right now." }),
        };
    }

    private ObjectResult TooLarge() =>
        StatusCode(StatusCodes.Status413PayloadTooLarge,
            new { error = "too-large", message = $"A Final Report can be at most {FinalReportStore.MaxBytes / 1024 / 1024} MB." });
}
