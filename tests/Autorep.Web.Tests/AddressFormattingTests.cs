using Autorep.Web.Services;
using FluentAssertions;

namespace Autorep.Web.Tests;

public class AddressFormattingTests
{
    [Theory]
    [InlineData("Wellington 6011", "6011", "Wellington")]
    [InlineData("Auckland 1010", "1010", "Auckland")]
    [InlineData("Palmerston North 4410", "4410", "Palmerston North")]
    [InlineData("Christchurch", null, "Christchurch")]
    [InlineData(null, "6011", null)]
    [InlineData("  ", "6011", null)]
    public void TownFromCityLine_strips_trailing_postcode(string? cityLine, string? postcode, string? expected)
    {
        AddressFormatting.TownFromCityLine(cityLine, postcode).Should().Be(expected);
    }
}
