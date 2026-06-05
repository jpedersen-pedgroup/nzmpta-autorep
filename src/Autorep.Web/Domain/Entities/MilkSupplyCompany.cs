namespace Autorep.Web.Domain.Entities;

/// <summary>
/// A dairy processor that a Farm supplies (e.g. Fonterra, Synlait). Reference data,
/// managed by the NZMPTA Super-Administrator. Distinct from a Testing Company.
/// </summary>
public class MilkSupplyCompany
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;

    // Address & contact (all optional).
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? Town { get; set; }
    public string? PostCode { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }

    // Logo image, stored in the DB and served via /Admin/MilkSupplyCompanies/Logo/{id}.
    public byte[]? LogoData { get; set; }
    public string? LogoContentType { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
