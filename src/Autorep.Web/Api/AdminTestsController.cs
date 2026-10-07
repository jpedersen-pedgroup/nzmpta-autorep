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
