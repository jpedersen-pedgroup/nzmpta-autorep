using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

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

    // Seeds the reference-data lookups (NZ regions, dairy processors). Idempotent
    // (insert-if-absent by name) and run in every environment. NZMPTA can add or
    // deactivate rows later via the admin portal without conflicting with this.
    public static async Task ReferenceDataAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<AutorepDbContext>();

        // 16 official NZ regions, north to south.
        string[] regions =
        [
            "Northland", "Auckland", "Waikato", "Bay of Plenty", "Gisborne",
            "Hawke's Bay", "Taranaki", "Manawatū-Whanganui", "Wellington",
            "Tasman", "Nelson", "Marlborough", "West Coast", "Canterbury",
            "Otago", "Southland",
        ];
        for (var i = 0; i < regions.Length; i++)
        {
            var name = regions[i];
            if (!await db.Regions.AnyAsync(r => r.Name == name))
            {
                db.Regions.Add(new Region { Name = name, SortOrder = i + 1 });
            }
        }

        // Main NZ dairy processors — seeded once as defaults, then NZMPTA owns the
        // list via the admin portal. Seed only when the table is empty so admin
        // renames/deletes aren't resurrected on the next startup. (Regions above are
        // a fixed national list, so they stay always-ensured.)
        if (!await db.MilkSupplyCompanies.AnyAsync())
        {
            string[] processors =
            [
                "Fonterra", "Open Country Dairy", "Synlait", "Westland Milk Products",
                "Tatua", "Miraka", "Oceania Dairy", "Mataura Valley Milk",
                "Green Valley Dairies", "Goodman Fielder",
            ];
            foreach (var name in processors)
            {
                db.MilkSupplyCompanies.Add(new MilkSupplyCompany { Name = name });
            }
        }

        await db.SaveChangesAsync();
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
