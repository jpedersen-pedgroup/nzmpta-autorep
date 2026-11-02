namespace Autorep.Web.Domain.Entities;

/// <summary>
/// An entry in the OEM Specs (formerly the equipment catalogs) the Machine Configuration dropdowns
/// are built from: shells, liners, pulsator models, vacuum and releaser pumps (those three carry a
/// brand), milkline sizes, pulsator configurations and regulator types. Pulsator models and vacuum
/// pumps also carry the OEM spec their tests are judged against, as dated versions
/// (<see cref="SpecVersions"/>).
/// Seeded from the legacy catalogs; the SuperAdmin adds/renames/deactivates rows and edits specs, and
/// Devices sync the set down (bundled defaults are the offline fallback). Deactivating hides an
/// item from new tests without disturbing historical tests that reference its name.
/// </summary>
public class EquipmentItem
{
    public const string Shell = "Shell";
    public const string Liner = "Liner";
    public const string Pulsator = "Pulsator";
    public const string MilklineSize = "MilklineSize";
    public const string PulsatorConfiguration = "PulsatorConfiguration";
    public const string VacuumPump = "VacuumPump";
    public const string ReleaserPump = "ReleaserPump";
    public const string Regulator = "Regulator";
    public static readonly string[] Types =
        [Shell, Liner, Pulsator, MilklineSize, PulsatorConfiguration, VacuumPump, ReleaserPump, Regulator];

    /// <summary>Catalogs whose rows carry a manufacturer in <see cref="Brand"/>, driving the
    /// make -> model dropdowns on the Device.</summary>
    public static readonly string[] BrandedTypes = [Pulsator, VacuumPump, ReleaserPump];

    public static bool HasBrand(string type) => BrandedTypes.Contains(type);

    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>One of <see cref="Types"/>.</summary>
    public string Type { get; set; } = Shell;

    public string Name { get; set; } = string.Empty;

    /// <summary>Manufacturer/brand — used by the branded catalogs (see <see cref="BrandedTypes"/>),
    /// which drive the brand/make → model dropdowns.</summary>
    public string? Brand { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>The model's OEM spec over time, for the types that have one (<see cref="OemSpecs.HasSpec"/>).
    /// Empty when no spec has been entered — the Device then gives that model no spec verdict. Read
    /// through <see cref="OemSpecs"/>.</summary>
    public List<EquipmentSpecVersion> SpecVersions { get; set; } = [];

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
