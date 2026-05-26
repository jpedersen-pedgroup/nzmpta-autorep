namespace Autorep.Web.Domain;

public static class Roles
{
    public const string Tester = "Tester";
    public const string CompanyAdministrator = "CompanyAdministrator";
    public const string SuperAdministrator = "SuperAdministrator";

    public static readonly string[] All = [Tester, CompanyAdministrator, SuperAdministrator];
}
