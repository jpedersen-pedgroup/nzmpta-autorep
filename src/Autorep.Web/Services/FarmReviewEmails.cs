using System.Globalization;
using System.Net;
using System.Text;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Services;

/// <summary>
/// Emails a farm's reviewers once, if it's still under review a week after a tester set it up in the
/// field (Josh, 12 Nov 2026). Nothing is emailed when the farm is added: the admin portal shows it
/// straight away (the farms list's alert, the home tile's badge), then the 48-hour banner
/// (<see cref="FarmReviewReminders"/>).
/// <list type="bullet">
/// <item>The reviewers are the people the banner shows it to: the creating company's Company
/// Administrators, or the Super Administrators for a farm whose creator had no Testing Company.
/// Deactivated accounts and accounts with no email address are left out.</item>
/// <item>An administrator who has dismissed the farm's banner has seen it, so isn't emailed.</item>
/// <item>Each administrator gets one email listing all of their farms that are due.</item>
/// <item>One email per review. The farms are stamped (<see cref="Farm.ReviewEmailSentAt"/>) before
/// anything is sent, so a restart part-way through can't send it twice; a send that fails is logged,
/// not retried. A farm put back under review later is emailed about again.</item>
/// </list>
/// <see cref="FarmReviewEmailService"/> runs it every hour.
/// </summary>
public class FarmReviewEmails
{
    /// <summary>How long a farm sits under review before its reviewers are emailed.</summary>
    public static readonly TimeSpan Delay = TimeSpan.FromDays(7);

    private readonly AutorepDbContext _db;
    private readonly IEmailSender _email;
    private readonly IConfiguration _config;
    private readonly ILogger<FarmReviewEmails> _log;

    public FarmReviewEmails(AutorepDbContext db, IEmailSender email, IConfiguration config, ILogger<FarmReviewEmails> log)
    {
        _db = db;
        _email = email;
        _config = config;
        _log = log;
    }

    private sealed record Reviewer(
        string Id, string Email, string? DisplayName, Guid? CompanyId, bool CompanyAdmin, bool SuperAdmin);

    /// <summary>Emails the reviewers of every farm that's due at <paramref name="now"/>. Returns the
    /// number of emails sent.</summary>
    public async Task<int> SendDueAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var dueSince = now - Delay;
        var farms = await _db.Farms
            .Where(f => f.PendingReviewSince != null && f.PendingReviewSince <= dueSince
                && (f.ReviewEmailSentAt == null || f.ReviewEmailSentAt < f.PendingReviewSince))
            .OrderBy(f => f.PendingReviewSince).ThenBy(f => f.Name)
            .ToListAsync(ct);
        if (farms.Count == 0) return 0;

        // Stamped before anything is sent: a restart part-way through then can't email them again.
        foreach (var farm in farms) farm.ReviewEmailSentAt = now;
        await _db.SaveChangesAsync(ct);

        var byId = farms.ToDictionary(f => f.Id);
        var farmIds = byId.Keys.ToList();
        var companyIds = farms.Select(f => f.CreatedByTestingCompanyId).Where(c => c != null).Distinct().ToList();
        var anyCompanyless = farms.Any(f => f.CreatedByTestingCompanyId == null);

        var roleRows = await (
            from u in _db.Users
            join ur in _db.UserRoles on u.Id equals ur.UserId
            join r in _db.Roles on ur.RoleId equals r.Id
            where u.Email != null
                && ((r.Name == Roles.CompanyAdministrator && companyIds.Contains(u.TestingCompanyId))
                    || (anyCompanyless && r.Name == Roles.SuperAdministrator))
            select new { u.Id, u.Email, u.DisplayName, u.TestingCompanyId, u.LockoutEnd, Role = r.Name })
            .ToListAsync(ct);
        var reviewers = roleRows
            .Where(x => !string.IsNullOrWhiteSpace(x.Email) && !AccountLockout.IsDeactivated(x.LockoutEnd))
            .GroupBy(x => x.Id)
            .Select(g => new Reviewer(
                g.Key, g.First().Email!.Trim(), g.First().DisplayName, g.First().TestingCompanyId,
                CompanyAdmin: g.Any(x => x.Role == Roles.CompanyAdministrator),
                SuperAdmin: g.Any(x => x.Role == Roles.SuperAdministrator)))
            .ToList();

        // Only a dismissal made during this review counts; an earlier review's doesn't.
        var dismissed = (await _db.FarmReviewDismissals
                .Where(d => farmIds.Contains(d.FarmId))
                .Select(d => new { d.FarmId, d.UserId, d.DismissedAt })
                .ToListAsync(ct))
            .Where(d => d.DismissedAt >= byId[d.FarmId].PendingReviewSince)
            .Select(d => (d.FarmId, d.UserId))
            .ToHashSet();

        var creatorIds = farms.Select(f => f.CreatedByTesterId).OfType<string>().Distinct().ToList();
        var creators = await _db.Users
            .Where(u => creatorIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.UserName })
            .ToDictionaryAsync(u => u.Id, u => string.IsNullOrWhiteSpace(u.DisplayName) ? u.UserName : u.DisplayName, ct);

        foreach (var farm in farms.Where(f => !reviewers.Any(r => Reviews(r, f))))
        {
            // It stays under review and on the Farms page for whoever can see it; nobody was emailed.
            _log.LogWarning(
                "Farm {FarmId} has been under review for a week but no administrator with an email address " +
                "was found to tell (company {CompanyId}).", farm.Id, farm.CreatedByTestingCompanyId);
        }

        var baseUrl = PublicUrl(_config);
        var sent = 0;
        foreach (var reviewer in reviewers)
        {
            var theirs = farms.Where(f => Reviews(reviewer, f) && !dismissed.Contains((f.Id, reviewer.Id))).ToList();
            if (theirs.Count == 0) continue;
            var (subject, html) = Compose(reviewer, theirs, creators, baseUrl);
            try
            {
                await _email.SendEmailAsync(reviewer.Email, subject, html);
                sent++;
            }
            catch (Exception ex)
            {
                // Per recipient, so one bad address or throttled send can't stop the rest.
                _log.LogWarning(ex, "Failed to send the farm-review email about {Farms} farms to one administrator.", theirs.Count);
            }
        }

        _log.LogInformation(
            "Farm review: {Farms} farms reached a week under review; {Emails} emails sent.", farms.Count, sent);
        return sent;
    }

    private static bool Reviews(Reviewer reviewer, Farm farm) =>
        farm.CreatedByTestingCompanyId is { } companyId
            ? reviewer.CompanyAdmin && reviewer.CompanyId == companyId
            : reviewer.SuperAdmin;

    private static (string Subject, string Html) Compose(
        Reviewer to, IReadOnlyList<Farm> farms, IReadOnlyDictionary<string, string?> creators, string baseUrl)
    {
        var one = farms.Count == 1;
        var subject = one ? $"Farm still awaiting review: {farms[0].Name}" : $"{farms.Count} farms still awaiting review";

        // Names are typed by testers, so they're encoded: they can't add markup to the email.
        var html = new StringBuilder();
        html.Append($"<p>Hi {(string.IsNullOrWhiteSpace(to.DisplayName) ? "there" : Encode(to.DisplayName))},</p>");
        html.Append(one
            ? "<p>This farm was set up by a tester out in the field more than a week ago and is still under review. " +
              "Please check its details, correct anything that's wrong, and approve it.</p>"
            : "<p>These farms were set up by testers out in the field more than a week ago and are still under review. " +
              "Please check their details, correct anything that's wrong, and approve them.</p>");
        html.Append("<ul>");
        foreach (var farm in farms)
        {
            var addedBy = farm.CreatedByTesterId is { } id && creators.GetValueOrDefault(id) is { Length: > 0 } name
                ? name
                : "a tester";
            var addedOn = farm.PendingReviewSince!.Value.ToNz().ToString("d MMM yyyy", CultureInfo.InvariantCulture);
            html.Append($"<li><a href=\"{baseUrl}/Admin/Farms/Edit/{farm.Id}\">{Encode(farm.Name)}</a>, " +
                        $"added by {Encode(addedBy)} on {addedOn}");
            // No Testing Company means no Company Administrator can see the farm, which is why it
            // reached a Super Administrator.
            if (farm.CreatedByTestingCompanyId is null)
                html.Append(". The tester isn't linked to a Testing Company, so no Company Administrator can see " +
                            "this farm: it needs an MPNZ administrator to review it");
            html.Append("</li>");
        }
        html.Append("</ul>");
        html.Append(one
            ? "<p>Testing against the farm isn't held up in the meantime. This is the only email about it; " +
              "until it's approved it stays on the Farms page in the admin portal.</p>"
            : "<p>Testing against them isn't held up in the meantime. This is the only email about them; " +
              "until they're approved they stay on the Farms page in the admin portal.</p>");
        return (subject, html.ToString());
    }

    private static string Encode(string text) => WebUtility.HtmlEncode(text);

    /// <summary>
    /// The app's address, for the links in the email. There's no request to take it from, so it's
    /// <c>App:PublicUrl</c> when that's set (a custom domain), otherwise the host name App Service
    /// gives the app (<c>WEBSITE_HOSTNAME</c>). Neither is set in local development, where the links
    /// are left relative: mail there only goes to the log.
    /// </summary>
    public static string PublicUrl(IConfiguration config)
    {
        var configured = config["App:PublicUrl"];
        if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim().TrimEnd('/');
        var host = config["WEBSITE_HOSTNAME"];
        return string.IsNullOrWhiteSpace(host) ? "" : $"https://{host.Trim()}";
    }
}

/// <summary>
/// Runs <see cref="FarmReviewEmails"/> two minutes after startup and then every hour. App Service
/// keeps the app running (Always On) on one instance, so the hourly check holds. A check that fails is
/// logged and the next one tries again; nothing it meets can take the app down.
/// </summary>
public sealed class FarmReviewEmailService : BackgroundService
{
    private static readonly TimeSpan StartDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<FarmReviewEmailService> _log;

    public FarmReviewEmailService(IServiceScopeFactory scopes, ILogger<FarmReviewEmailService> log)
    {
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartDelay, stoppingToken);
            using var timer = new PeriodicTimer(Interval);
            do
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<FarmReviewEmails>()
                        .SendDueAsync(DateTimeOffset.UtcNow, stoppingToken);
                }
                catch (Exception e) when (!stoppingToken.IsCancellationRequested)
                {
                    _log.LogError(e, "The farm-review email check failed; it runs again in an hour");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }
}
