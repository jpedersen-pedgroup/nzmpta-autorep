using Autorep.Web.Domain;

namespace Mpnz.AutoRep.Migration.Mapping;

/// <summary>
/// The roles a legacy <c>dbo.Users</c> row gets in the new app. The legacy table holds one
/// <c>UserType</c> per person (1 Tester, 2 Company Administrator, 3 Super Administrator), but some
/// Company Administrators also test. In the new app they need both roles: without Tester they can't
/// open the tester app or sync. MPNZ's licence list (Maria, 11 Nov 2026) marked 21 people as both.
/// Each of them has a certificate number or a licence expiry, and none of the 44 admin-only rows
/// has either. So a UserType 2 row with either one is migrated as Company Administrator + Tester.
/// </summary>
public static class LegacyRoles
{
    /// <param name="userType">Legacy <c>Users.UserType</c>; anything but 2 or 3 is a Tester, as before.</param>
    /// <param name="certificateNo">Legacy <c>Users.CertificateNo</c>, already trimmed (blank means none).</param>
    /// <param name="licenceExpiry">The licence expiry after the 1900/2030 sentinels are dropped.</param>
    public static IReadOnlyList<string> For(int? userType, string? certificateNo, DateOnly? licenceExpiry) =>
        userType switch
        {
            2 when HoldsLicence(certificateNo, licenceExpiry) => [Roles.CompanyAdministrator, Roles.Tester],
            2 => [Roles.CompanyAdministrator],
            3 => [Roles.SuperAdministrator],
            _ => [Roles.Tester],
        };

    private static bool HoldsLicence(string? certificateNo, DateOnly? licenceExpiry) =>
        !string.IsNullOrWhiteSpace(certificateNo) || licenceExpiry is not null;
}
