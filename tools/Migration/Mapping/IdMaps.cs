namespace Nzmpta.AutoRep.Migration.Mapping;

/// <summary>In-memory legacy-id -> new-id maps, populated in FK order and consumed downstream.</summary>
public sealed class IdMaps
{
    /// <summary>legacy Companies.ID -> TestingCompany.Id</summary>
    public Dictionary<int, Guid> Company { get; } = new();

    /// <summary>legacy Users.ID -> AspNetUsers.Id (string)</summary>
    public Dictionary<int, string> User { get; } = new();

    /// <summary>normalised farm natural key -> Farm.Id</summary>
    public Dictionary<string, Guid> Farm { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The synthetic "Legacy/Unknown Tester" account, used to keep owner-orphan tests.</summary>
    public string SyntheticUnknownTesterId { get; set; } = "";
}
