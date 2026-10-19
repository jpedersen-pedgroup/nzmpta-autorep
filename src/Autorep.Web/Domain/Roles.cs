namespace Autorep.Web.Domain;

public static class Roles
{
    public const string Tester = "Tester";
    public const string CompanyAdministrator = "CompanyAdministrator";
    public const string SuperAdministrator = "SuperAdministrator";

    public static readonly string[] All = [Tester, CompanyAdministrator, SuperAdministrator];

    /// <summary>Why a user holding <paramref name="roles"/> can't be left without a Testing Company, or
    /// null when they can. A Tester works for one and a Company Administrator runs one; a Super
    /// Administrator works across all of them, so needs none.</summary>
    public static string? CompanyRequiredError(IEnumerable<string> roles, Guid? testingCompanyId)
    {
        if (testingCompanyId is not null) return null;
        var needing = new[] { Tester, CompanyAdministrator }.Where(roles.Contains).ToList();
        return needing.Count switch
        {
            0 => null,
            1 => $"{Label(needing[0])}s must be assigned to a Testing Company.",
            _ => "Testers and Company Administrators must be assigned to a Testing Company.",
        };
    }

    /// <summary>Human-friendly label for a role key (e.g. "CompanyAdministrator" → "Company Administrator").</summary>
    public static string Label(string role) => role switch
    {
        Tester => "Tester",
        CompanyAdministrator => "Company Administrator",
        SuperAdministrator => "Super Administrator",
        _ => role
    };
}
