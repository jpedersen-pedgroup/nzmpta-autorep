namespace Autorep.Web.Domain.Entities;

/// <summary>
/// One of the official New Zealand regions. Reference data — kept as a constrained
/// lookup so NZMPTA-wide reporting groups Farms by a single canonical region value.
/// Grouped by <see cref="Island"/> (North / South) for display. Cached on Devices.
/// </summary>
public class Region
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;

    /// <summary>"North Island" or "South Island" — used to group the region pickers.</summary>
    public string Island { get; set; } = string.Empty;

    /// <summary>Display order (roughly north to south).</summary>
    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
