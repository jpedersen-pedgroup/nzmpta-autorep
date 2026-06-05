namespace Autorep.Web.Domain.Entities;

/// <summary>
/// One of the official New Zealand regions. Reference data — kept as a constrained
/// lookup so NZMPTA-wide reporting groups Farms by a single canonical region value.
/// Cached on Devices with the rest of the reference data for offline use.
/// </summary>
public class Region
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;

    /// <summary>Display order (roughly north to south).</summary>
    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
