using Autorep.Web.Domain;
using FluentAssertions;
using Mpnz.AutoRep.Migration.Mapping;

namespace Autorep.Web.Tests;

/// <summary>
/// The roles the migration gives each legacy user (tools/Migration/Mapping/LegacyRoles). A Company
/// Administrator who holds a licence also tests, so must arrive with the Tester role too: Maria's
/// 11 Nov 2026 list had 21 of them, and without it they can't open the tester app or sync.
/// </summary>
public class LegacyRolesTests
{
    private static readonly DateOnly Expiry = new(2027, 3, 14);

    [Theory]
    [InlineData(1, null, false, new[] { Roles.Tester })]
    [InlineData(1, "PC123", true, new[] { Roles.Tester })]
    [InlineData(null, null, false, new[] { Roles.Tester })]
    [InlineData(9, null, false, new[] { Roles.Tester })]
    [InlineData(3, null, false, new[] { Roles.SuperAdministrator })]
    [InlineData(3, "PC123", true, new[] { Roles.SuperAdministrator })]
    [InlineData(2, null, false, new[] { Roles.CompanyAdministrator })]
    [InlineData(2, "  ", false, new[] { Roles.CompanyAdministrator })]
    [InlineData(2, "PC123", true, new[] { Roles.CompanyAdministrator, Roles.Tester })]
    [InlineData(2, "PC123", false, new[] { Roles.CompanyAdministrator, Roles.Tester })]
    [InlineData(2, "Prov Til", false, new[] { Roles.CompanyAdministrator, Roles.Tester })]
    [InlineData(2, null, true, new[] { Roles.CompanyAdministrator, Roles.Tester })]
    public void Legacy_user_type_and_licence_decide_the_roles(int? userType, string? certificateNo, bool hasExpiry, string[] expected)
    {
        LegacyRoles.For(userType, certificateNo, hasExpiry ? Expiry : null).Should().Equal(expected);
    }
}
