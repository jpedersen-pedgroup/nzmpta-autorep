namespace Autorep.Web.Domain.Entities;

/// <summary>
/// An entry in an equipment catalog the Machine Configuration dropdowns are built from:
/// shells, liners, pulsator models, vacuum and releaser pumps (those three carry a brand),
/// milkline sizes, pulsator configurations and regulator types.
/// Seeded from the legacy catalogs; the SuperAdmin adds/renames/deactivates rows and Devices
/// sync the active set down (bundled defaults are the offline fallback). Deactivating hides an
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

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
