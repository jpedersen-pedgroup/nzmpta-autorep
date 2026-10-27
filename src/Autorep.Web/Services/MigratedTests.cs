using System.Globalization;

namespace Autorep.Web.Services;

/// <summary>
/// Tests migrated from AutoRep Plus carry a banner saying so: "Migrated from AutoRep Plus — tests
/// before {date}", the date AutoRep Plus stopped taking tests. That date is one app setting,
/// <c>LegacyTests:CutoffDate</c> (yyyy-MM-dd). While it's unset the banner leaves the date out.
/// The wizard's pages hand it to the client as <c>data-migrated-cutoff</c> (Client/wizard/migrated.ts).
/// </summary>
public static class MigratedTests
{
    public const string CutoffSetting = "LegacyTests:CutoffDate";

    /// <summary>The cutoff, or null while it's unset or isn't a yyyy-MM-dd date.</summary>
    public static DateOnly? Cutoff(IConfiguration config) =>
        DateOnly.TryParseExact(config[CutoffSetting]?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date)
            ? date
            : null;

    /// <summary>The page attribute's value: the date, or empty while it's unset (an empty attribute
    /// tells the device to forget a date it remembered).</summary>
    public static string CutoffAttribute(IConfiguration config) =>
        Cutoff(config)?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
}
