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
public class AdminTestsController(AutorepDbContext db, AdminVersioning versioning, UserManager<Tester> users)
    : ControllerBase
{
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
