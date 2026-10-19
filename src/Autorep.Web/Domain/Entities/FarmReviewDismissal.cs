namespace Autorep.Web.Domain.Entities;

/// <summary>
/// An administrator has dismissed the admin portal's reminder that a farm has been under review for
/// 48 hours (Services/FarmReviewReminders). Per user, per farm, and kept on the server so it holds on
/// every device they sign in on. It covers the review that was under way when it was made: one older
/// than the farm's <see cref="Farm.PendingReviewSince"/> doesn't hide a later review. Approving the
/// farm ends the reminder for everyone, whether or not they dismissed it.
/// </summary>
public class FarmReviewDismissal
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string UserId { get; set; } = string.Empty;
    public Tester? User { get; set; }

    public Guid FarmId { get; set; }
    public Farm? Farm { get; set; }

    public DateTimeOffset DismissedAt { get; set; } = DateTimeOffset.UtcNow;
}
