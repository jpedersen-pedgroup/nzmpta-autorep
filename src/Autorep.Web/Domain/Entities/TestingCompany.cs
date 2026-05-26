namespace Autorep.Web.Domain.Entities;

public class TestingCompany
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<Tester> Testers { get; set; } = new List<Tester>();
}
