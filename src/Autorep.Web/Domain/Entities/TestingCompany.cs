namespace Autorep.Web.Domain.Entities;

public class TestingCompany
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

    // Report branding — the testing firm's logo shown on generated reports
    // (mirrors MilkSupplyCompany). Migrated from legacy Companies.ImagePath.
    public byte[]? LogoData { get; set; }
    public string? LogoContentType { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<Tester> Testers { get; set; } = new List<Tester>();
}
