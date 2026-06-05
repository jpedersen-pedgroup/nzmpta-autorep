namespace Autorep.Web.Services;

public static class AddressFormatting
{
    /// <summary>
    /// NZ Post returns the city line as "City Postcode" (e.g. "Wellington 6011").
    /// Strip the trailing postcode to get the town. Returns null when there's nothing left.
    /// </summary>
    public static string? TownFromCityLine(string? cityLine, string? postcode)
    {
        if (string.IsNullOrWhiteSpace(cityLine)) return null;
        var town = cityLine;
        if (!string.IsNullOrWhiteSpace(postcode))
            town = town.Replace(postcode, "").Trim();
        return string.IsNullOrWhiteSpace(town) ? null : town;
    }
}
