namespace Autorep.Web.Domain.Entities;

/// <summary>
/// A dairy processor that a Farm supplies (e.g. Fonterra, Synlait). Reference data,
/// managed by the NZMPTA Super-Administrator and cached on Devices for offline use.
/// Distinct from a Testing Company (which employs Testers).
/// </summary>
public class MilkSupplyCompany
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
