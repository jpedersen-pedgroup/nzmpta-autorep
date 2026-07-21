using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Services;

/// <summary>
/// Emails a Testing Company's administrators when one of their testers sets up a farm in the
/// field (New-test modal or offline sync push), so they can review and approve the details.
/// Best-effort: a mail failure is logged and swallowed — it must never fail the farm creation
/// or the sync push that triggered it.
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
            if (farm.CreatedByTestingCompanyId is null) return;

            var admins = await (
                from u in _db.Users
                join ur in _db.UserRoles on u.Id equals ur.UserId
                join r in _db.Roles on ur.RoleId equals r.Id
                where r.Name == Roles.CompanyAdministrator
                    && u.TestingCompanyId == farm.CreatedByTestingCompanyId
                    && u.Email != null
                select new { u.Email, u.DisplayName }).ToListAsync(ct);
            if (admins.Count == 0)
            {
                _log.LogInformation(
                    "Farm {FarmId} is pending review but company {CompanyId} has no administrators to notify.",
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
            foreach (var admin in admins)
            {
                var greeting = string.IsNullOrWhiteSpace(admin.DisplayName)
                    ? "there"
                    : System.Net.WebUtility.HtmlEncode(admin.DisplayName);
                await _email.SendEmailAsync(admin.Email!,
                    $"Farm awaiting review: {farm.Name}",
                    $"<p>Hi {greeting},</p>" +
                    $"<p>{creatorName} set up the farm <strong>{farmName}</strong> while out in the field, " +
                    "so it's flagged as under review. Please check the details, correct anything that's wrong, and approve it. " +
                    "Testing against the farm isn't held up in the meantime.</p>" +
                    $"<p><a href=\"{reviewUrl}\">Review farm details</a></p>");
            }
        }
        catch (Exception ex)
        {
            // Notification is best-effort; the farm (and any test push carrying it) must stand.
            _log.LogWarning(ex, "Failed to send farm-review notification for farm {FarmId}.", farm.Id);
        }
    }
}
