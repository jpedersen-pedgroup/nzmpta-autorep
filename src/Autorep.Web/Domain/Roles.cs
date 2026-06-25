namespace Autorep.Web.Domain;

public static class Roles
{
    public const string Tester = "Tester";
    public const string CompanyAdministrator = "CompanyAdministrator";
    public const string SuperAdministrator = "SuperAdministrator";

    public static readonly string[] All = [Tester, CompanyAdministrator, SuperAdministrator];

    /// <summary>Human-friendly label for a role key (e.g. "CompanyAdministrator" → "Company Administrator").</summary>
    public static string Label(string role) => role switch
    {
        Tester => "Tester",
        CompanyAdministrator => "Company Administrator",
        SuperAdministrator => "Super Administrator",
        _ => role
    };
}
