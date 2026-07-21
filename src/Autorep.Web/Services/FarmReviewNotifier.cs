using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Services;

/// <summary>
/// Emails a Testing Company's administrators when one of their testers sets up a farm in the
/// field (New-test modal or offline sync push), so they can review and approve the details.
/// A farm whose creator has no Testing Company escalates to the Super-Administrators instead —
/// see <see cref="RecipientsAsync"/>. Best-effort: a mail failure is logged and swallowed — it
/// must never fail the farm creation or the sync push that triggered it.
/// </summary>
public class FarmReviewNotifier
{
    private readonly AutorepDbContext _db;
    private readonly IEmailSender _email;
    private readonly ILogger<FarmReviewNotifier> _log;

    public FarmReviewNotifier(AutorepDbContext db, IEmailSender email, ILogger<FarmReviewNotifier> log)
    {
        _db = db;
        _email = email;
        _log = log;
    }

    public async Task NotifyPendingFarmAsync(Farm farm, string reviewUrl, CancellationToken ct = default)
    {
        try
        {
            var admins = await RecipientsAsync(farm, ct);
            if (admins.Count == 0)
            {
                // The farm still carries PendingReviewSince, so it stays in the Admin review queue
                // for whoever can see it — this only means nobody was emailed.
                _log.LogWarning(
                    "Farm {FarmId} is pending review but no administrator with an email address was " +
                    "found to notify (company {CompanyId}).",
                    farm.Id, farm.CreatedByTestingCompanyId);
                return;
            }

            var creatorRow = farm.CreatedByTesterId is null
                ? null
                : await _db.Users.Where(u => u.Id == farm.CreatedByTesterId)
                    .Select(u => new { u.DisplayName, u.UserName }).FirstOrDefaultAsync(ct);
            var creator = string.IsNullOrWhiteSpace(creatorRow?.DisplayName)
                ? creatorRow?.UserName
                : creatorRow!.DisplayName;

            // Names are tester-typed input — encode them so they can't inject markup into the mail.
            var farmName = System.Net.WebUtility.HtmlEncode(farm.Name);
            var creatorName = System.Net.WebUtility.HtmlEncode(creator ?? "A tester");
            // No Testing Company means no Company Administrator can reach the farm, so the mail
            // goes to the Super-Administrators and should say why they're the ones getting it.
            var orphanNote = farm.CreatedByTestingCompanyId is null
                ? "<p>This tester isn't linked to a Testing Company, so no Company Administrator " +
                  "can see this farm — it needs an NZMPTA administrator to review it.</p>"
                : "";

            foreach (var admin in admins)
            {
                var greeting = string.IsNullOrWhiteSpace(admin.DisplayName)
                    ? "there"
                    : System.Net.WebUtility.HtmlEncode(admin.DisplayName);
                try
                {
                    await _email.SendEmailAsync(admin.Email,
                        $"Farm awaiting review: {farm.Name}",
                        $"<p>Hi {greeting},</p>" +
                        $"<p>{creatorName} set up the farm <strong>{farmName}</strong> while out in the field, " +
                        "so it's flagged as under review. Please check the details, correct anything that's wrong, and approve it. " +
                        "Testing against the farm isn't held up in the meantime.</p>" +
                        orphanNote +
                        $"<p><a href=\"{reviewUrl}\">Review farm details</a></p>");
                }
                catch (Exception ex)
                {
                    // Per recipient, so one bad address or throttled send can't stop the rest.
                    _log.LogWarning(ex,
                        "Failed to send the farm-review notification for farm {FarmId} to one recipient.",
                        farm.Id);
                }
            }
        }
        catch (Exception ex)
        {
            // Notification is best-effort; the farm (and any test push carrying it) must stand.
            _log.LogWarning(ex, "Failed to send farm-review notification for farm {FarmId}.", farm.Id);
        }
    }

    private sealed record Recipient(string Email, string? DisplayName);

    /// <summary>
    /// Who reviews this farm. Normally the creating company's Company Administrators. A farm whose
    /// creator has no Testing Company matches neither arm of <see cref="FarmScope"/>, so no Company
    /// Administrator could ever see it — those escalate to the Super-Administrators, who see every
    /// farm, rather than sitting pending with nobody told.
    /// </summary>
    private async Task<List<Recipient>> RecipientsAsync(Farm farm, CancellationToken ct)
    {
        var escalate = farm.CreatedByTestingCompanyId is null;
        var role = escalate ? Roles.SuperAdministrator : Roles.CompanyAdministrator;

        return await (
            from u in _db.Users
            join ur in _db.UserRoles on u.Id equals ur.UserId
            join r in _db.Roles on ur.RoleId equals r.Id
            where r.Name == role
                && u.Email != null
                && (escalate || u.TestingCompanyId == farm.CreatedByTestingCompanyId)
            select new Recipient(u.Email!, u.DisplayName)).ToListAsync(ct);
    }
}
