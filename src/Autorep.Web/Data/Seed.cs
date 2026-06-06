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

        // 16 official NZ regions, north to south, grouped by island.
        (string Name, string Island)[] regions =
        [
            ("Northland", "North Island"), ("Auckland", "North Island"),
            ("Waikato", "North Island"), ("Bay of Plenty", "North Island"),
            ("Gisborne", "North Island"), ("Hawke's Bay", "North Island"),
            ("Taranaki", "North Island"), ("Manawatū-Whanganui", "North Island"),
            ("Wellington", "North Island"),
            ("Tasman", "South Island"), ("Nelson", "South Island"),
            ("Marlborough", "South Island"), ("West Coast", "South Island"),
            ("Canterbury", "South Island"), ("Otago", "South Island"),
            ("Southland", "South Island"),
        ];
        for (var i = 0; i < regions.Length; i++)
        {
            var (name, island) = regions[i];
            var existing = await db.Regions.FirstOrDefaultAsync(r => r.Name == name);
            if (existing is null)
            {
                db.Regions.Add(new Region { Name = name, Island = island, SortOrder = i + 1 });
            }
            else if (string.IsNullOrEmpty(existing.Island))
            {
                existing.Island = island; // backfill island on pre-split rows
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

    // Creates the initial NZMPTA Super-Administrator from configuration, when no account with
    // that email exists yet. No password lives in source: set Bootstrap:AdminEmail and
    // Bootstrap:AdminPassword (the password via Key Vault / an App Service secret). The
    // account is flagged for a forced password change on first login, so the bootstrap
    // password is single-use. Safe to leave configured — it's a no-op once the user exists.
    public static async Task BootstrapAdminAsync(IServiceProvider services)
    {
        var config = services.GetRequiredService<IConfiguration>();
        var email = config["Bootstrap:AdminEmail"];
        var password = config["Bootstrap:AdminPassword"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return; // not configured — no insecure default

        var userManager = services.GetRequiredService<UserManager<Tester>>();
        if (await userManager.FindByEmailAsync(email) is not null)
            return; // never overwrite or reset an existing account

        var user = new Tester
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = "NZMPTA Administrator",
            ForcedPasswordResetRequired = true,
        };
        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            services.GetRequiredService<ILoggerFactory>().CreateLogger("Bootstrap")
                .LogError("Failed to create bootstrap admin {Email}: {Errors}",
                    email, string.Join("; ", result.Errors.Select(e => e.Description)));
            return;
        }
        await userManager.AddToRoleAsync(user, Roles.SuperAdministrator);
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
