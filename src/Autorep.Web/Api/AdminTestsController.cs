using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Autorep.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

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
    AutorepDbContext db, AdminVersioning versioning, TestDeletion deletion, UserManager<Tester> users)
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
}
