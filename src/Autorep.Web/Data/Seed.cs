using Autorep.Web.Domain;
using Microsoft.AspNetCore.Identity;

namespace Autorep.Web.Data;

public static class Seed
{
    // Ensures the three application roles exist. Safe to call repeatedly.
    public static async Task RolesAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }
    }
}
