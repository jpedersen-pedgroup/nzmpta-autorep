using System.Security.Claims;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Services;

/// <summary>
/// The admin portal's reminder about farms still under review 48 hours after a tester set them up
/// in the field (Josh, 19 Oct). It goes to whoever reviews the farm, the same people
/// <see cref="FarmReviewNotifier"/> emails: the creating company's Company Administrators, or the
/// Super Administrators for a farm whose creator had no Testing Company. Each of them can dismiss a
/// farm for themselves (<see cref="FarmReviewDismissal"/>); approving the farm ends it for everyone.
/// The review itself is unchanged, and so are the farms list's alert and the home tile's badge.
/// </summary>
public static class FarmReviewReminders
{
    /// <summary>How long a farm sits under review before the reminder shows.</summary>
    public static readonly TimeSpan Delay = TimeSpan.FromHours(48);

    public sealed record Item(Guid FarmId, string FarmName, string? AddedBy, DateTimeOffset PendingSince);

    /// <summary>The farms <paramref name="user"/> is reminded about at <paramref name="now"/>, oldest first.</summary>
    public static async Task<IReadOnlyList<Item>> DueForAsync(
        AutorepDbContext db, ClaimsPrincipal user, DateTimeOffset now, CancellationToken ct = default)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var superAdmin = user.IsInRole(Roles.SuperAdministrator);
        var companyAdmin = user.IsInRole(Roles.CompanyAdministrator);
        if (userId is null || !(superAdmin || companyAdmin)) return [];

        // No company claim exists (it would go stale after a move), so this is a lookup.
        var companyId = companyAdmin
            ? await db.Users.Where(u => u.Id == userId).Select(u => u.TestingCompanyId).FirstOrDefaultAsync(ct)
            : null;
        if (!superAdmin && companyId is null) return [];

        var shownSince = now - Delay;
        return await db.Farms
            .Where(f => f.PendingReviewSince != null && f.PendingReviewSince <= shownSince)
            .Where(f => (superAdmin && f.CreatedByTestingCompanyId == null)
                || (companyId != null && f.CreatedByTestingCompanyId == companyId))
            .Where(f => !db.FarmReviewDismissals.Any(d =>
                d.FarmId == f.Id && d.UserId == userId && d.DismissedAt >= f.PendingReviewSince))
            .OrderBy(f => f.PendingReviewSince).ThenBy(f => f.Name)
            .Select(f => new Item(
                f.Id,
                f.Name,
                f.CreatedByTester == null ? null
                    : f.CreatedByTester.DisplayName != "" ? f.CreatedByTester.DisplayName : f.CreatedByTester.UserName,
                f.PendingReviewSince!.Value))
            .ToListAsync(ct);
    }

    /// <summary>Dismisses the reminder for these farms, for this user only. A farm the user isn't
    /// being reminded about is ignored, so a posted id can't create rows for anything else.</summary>
    public static async Task<int> DismissAsync(
        AutorepDbContext db, ClaimsPrincipal user, IEnumerable<Guid> farmIds, DateTimeOffset now,
        CancellationToken ct = default)
    {
        var due = (await DueForAsync(db, user, now, ct)).Select(i => i.FarmId).ToHashSet();
        var dismiss = farmIds.Where(due.Contains).Distinct().ToList();
        if (dismiss.Count == 0) return 0;

        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var earlier = await db.FarmReviewDismissals
            .Where(d => d.UserId == userId && dismiss.Contains(d.FarmId))
            .ToDictionaryAsync(d => d.FarmId, ct);
        foreach (var farmId in dismiss)
        {
            if (earlier.TryGetValue(farmId, out var row)) row.DismissedAt = now; // an earlier review's
            else db.FarmReviewDismissals.Add(new FarmReviewDismissal { UserId = userId, FarmId = farmId, DismissedAt = now });
        }
        await db.SaveChangesAsync(ct);
        return dismiss.Count;
    }
}
