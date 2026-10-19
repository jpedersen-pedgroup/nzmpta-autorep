namespace Autorep.Web.Domain.Entities;

/// <summary>
/// A one-off data seed that has run against this database. Most reference data is seeded
/// insert-if-absent on every start, which can't express "do this once": a seed that adds rows an
/// administrator may later rename or delete would bring them back on the next start. Such a seed
/// records its key here when it runs and never runs again.
/// </summary>
public class AppliedSeed
{
    public string Key { get; set; } = string.Empty;

    public DateTimeOffset AppliedAt { get; set; } = DateTimeOffset.UtcNow;
}
