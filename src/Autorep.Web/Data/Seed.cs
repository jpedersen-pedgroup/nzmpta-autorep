using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
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

    // Development-only: creates a default Super-Administrator and Tester
    // so you can sign in immediately after first run. NEVER call this in
    // staging or prod.
    public static async Task DevUsersAsync(IServiceProvider services)
    {
        var userManager = services.GetRequiredService<UserManager<Tester>>();

        await EnsureUser(userManager, "admin@local",  "DevPassword123!", "Local Super Admin", Roles.SuperAdministrator);
        await EnsureUser(userManager, "tester@local", "DevPassword123!", "Local Tester",      Roles.Tester);
    }

    private static async Task EnsureUser(
        UserManager<Tester> userManager,
        string email,
        string password,
        string displayName,
        string role)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new Tester
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = displayName
            };
            var result = await userManager.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to seed dev user {email}: " +
                    string.Join("; ", result.Errors.Select(e => e.Description)));
            }
        }
        if (!await userManager.IsInRoleAsync(user, role))
        {
            await userManager.AddToRoleAsync(user, role);
        }
    }
}
