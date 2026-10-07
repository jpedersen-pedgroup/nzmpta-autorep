namespace Nzmpta.AutoRep.Migration.Mapping;

/// <summary>In-memory legacy-id -> new-id maps, populated in FK order and consumed downstream.</summary>
public sealed class IdMaps
{
    /// <summary>legacy Companies.ID -> TestingCompany.Id</summary>
    public Dictionary<int, Guid> Company { get; } = new();

    /// <summary>legacy Users.ID -> AspNetUsers.Id (string)</summary>
    public Dictionary<int, string> User { get; } = new();

    /// <summary>legacy Users.ID -> the tester's TestingCompany.Id (null when their company didn't
    /// map). Stamped onto each migrated test as MachineTest.TestingCompanyId: company-scoped
    /// lists and the report's company branding filter on that column, not on the tester.</summary>
    public Dictionary<int, Guid?> UserCompany { get; } = new();

    /// <summary>normalised farm natural key -> Farm.Id</summary>
    public Dictionary<string, Guid> Farm { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The synthetic "Legacy/Unknown Tester" account, used to keep owner-orphan tests.</summary>
    public string SyntheticUnknownTesterId { get; set; } = "";
}
