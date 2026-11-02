using Autorep.Web.Domain;
using Microsoft.AspNetCore.Hosting;

namespace Autorep.Web.Tests;

// The two-factor requirement as PRD story 58 has it - Super-Administrators must enrol - turned on
// through configuration as it would be in Azure. Off by default since 3 Nov 2026 (see MfaOptions);
// these keep the enforcement tested for when MPNZ wants it back.

public class MfaRequiredCookieAuthWebAppFactory : CookieAuthWebAppFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting($"{MfaOptions.Section}:RequiredRoles:0", Roles.SuperAdministrator);
    }
}

public class MfaRequiredAuthedWebAppFactory : AuthedWebAppFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting($"{MfaOptions.Section}:RequiredRoles:0", Roles.SuperAdministrator);
    }
}
