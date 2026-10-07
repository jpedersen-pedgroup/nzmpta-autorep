using System.Text.Json.Nodes;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Autorep.Web.Tests.E2E;

// The offline tester factory's world — a tester with a device, their company and its farms — plus
// the two people who edit tests from the admin portal: a Super-Administrator (two-factor enrolled, as
// the role requires) and a Company Administrator at the tester's company. Each case seeds the
// completed test it works on, so cases don't depend on each other's edits.
public class AdminEditE2EWebAppFactory : OfflineE2EWebAppFactory
{
    public const string AdminEmail = "e2e-admin@local";
    public const string AdminName = "Sam Superadmin";
    public const string CompanyAdminEmail = "e2e-coadmin@local";
    public const string CompanyAdminName = "Cora Companyadmin";

    public string AdminId { get; private set; } = "";
    public string CompanyAdminId { get; private set; } = "";
    /// <summary>The Super-Administrator's Base32 authenticator key, for <see cref="Totp"/>.</summary>
    public string AdminAuthenticatorKey { get; private set; } = "";

    protected override async Task SeedMoreAsync(IServiceProvider services)
    {
        var users = services.GetRequiredService<UserManager<Tester>>();

        var admin = await CreateAsync(users, AdminEmail, AdminName, null, Roles.SuperAdministrator);
        await users.ResetAuthenticatorKeyAsync(admin);
        await users.SetTwoFactorEnabledAsync(admin, true);
        AdminAuthenticatorKey = (await users.GetAuthenticatorKeyAsync(admin))!;
        AdminId = admin.Id;

        CompanyAdminId = (await CreateAsync(users, CompanyAdminEmail, CompanyAdminName, CompanyId, Roles.CompanyAdministrator)).Id;
    }

    private static async Task<Tester> CreateAsync(UserManager<Tester> users, string email, string name, Guid? companyId, string role)
    {
        var user = new Tester
        {
            UserName = email, Email = email, EmailConfirmed = true, DisplayName = name, TestingCompanyId = companyId,
            // Pre-accepted, so sign-in doesn't divert to /Account/AcceptTerms.
            TermsAcceptedVersion = Seed.DefaultTermsVersion, TermsAcceptedAt = DateTimeOffset.UtcNow,
        };
        var created = await users.CreateAsync(user, TesterPassword);
        if (!created.Succeeded)
            throw new InvalidOperationException(string.Join("; ", created.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(user, role);
        return user;
    }

    /// <summary>A completed version-1 test of the tester's at a farm of its own, carrying the capture
    /// payload a device would have sent: a fault with its recommendation, readings, comments.
    /// <paramref name="shape"/> adjusts the payload before it's stored.</summary>
    public async Task<(Guid Id, Guid ClientId)> SeedCompletedTestAsync(string farmName, Action<JsonObject>? shape = null)
    {
        using var scope = AppServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
        var farm = new Farm { Name = farmName, CreatedByTestingCompanyId = CompanyId, SupplyNumber = "40999" };
        db.Farms.Add(farm);

        var clientId = Guid.NewGuid();
        var completed = DateTimeOffset.UtcNow.AddDays(-3);
        var at = completed.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
        var payload = new JsonObject
        {
            ["id"] = clientId.ToString(),
            ["farmId"] = farm.Id.ToString(),
            ["farmName"] = farmName,
            ["farm"] = new JsonObject { ["name"] = farmName, ["supplyNumber"] = "40999" },
            ["config"] = new JsonObject
            {
                ["plantType"] = "HerringboneLowline", ["clusterCount"] = 20, ["flushingPulsationSystem"] = false,
                ["pulsatorCount"] = 10, ["linerVented"] = false, ["numberOfVacuumPumps"] = 1,
                ["pumpLubrication"] = "OilLubricated", ["vsdFitted"] = false, ["isoPortsAvailable"] = true,
                ["hasPulsatorStopSystem"] = false, ["hasAcr"] = false, ["hasBailGates"] = false,
                ["hasMilkMeters"] = false, ["hasTeatSprayer"] = false, ["hasBackingGate"] = false,
                ["hasReleaserPump"] = false,
            },
            ["currentStep"] = "ReviewSignOff",
            ["visualFaults"] = new JsonObject
            {
                ["vp.wick"] = new JsonObject { ["status"] = "fault", ["severity"] = "Minor", ["observation"] = "Oil Wicks Dirty" },
            },
            ["attestations"] = new JsonArray
            {
                new JsonObject { ["step"] = "ReviewSignOff", ["attestedAt"] = at, ["text"] = "I confirm this test has been completed and the results are accurate." },
            },
            ["readings"] = new JsonObject { ["tr.workingVacuum"] = 48, ["tr.nominalVacuum"] = 50, ["tr.regulationDeviation"] = -2 },
            ["recommendations"] = new JsonObject { ["vp.wick"] = "Clean the wicks" },
            ["dataFields"] = new JsonObject(),
            ["notes"] = "Original comment from the farm",
            ["nextTestDate"] = DateOnly.FromDateTime(completed.UtcDateTime).AddYears(1).ToString("yyyy-MM-dd"),
            ["testedBy"] = new JsonObject { ["name"] = TesterName },
            ["testingCompanyId"] = CompanyId.ToString(),
            ["testingCompanyName"] = CompanyName,
            ["createdAt"] = completed.AddHours(-2).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"),
            ["updatedAt"] = at,
            ["markedCompleteAt"] = at,
            ["syncState"] = "uploaded",
            ["version"] = 1,
        };
        shape?.Invoke(payload);

        var test = new MachineTest
        {
            ClientId = clientId,
            RootClientId = clientId,
            TesterId = TesterId,
            TestingCompanyId = CompanyId,
            FarmId = farm.Id,
            CreatedAt = completed.AddHours(-2),
            UpdatedAt = completed,
            MarkedCompleteAt = completed,
            Notes = "Original comment from the farm",
            NextTestDate = DateOnly.FromDateTime(completed.UtcDateTime).AddYears(1),
            PayloadJson = payload.ToJsonString(),
            Configuration = new MachineConfiguration { PlantType = PlantType.HerringboneLowline, ClusterCount = 20, PulsatorCount = 10 },
        };
        db.MachineTests.Add(test);
        await db.SaveChangesAsync();
        return (test.Id, clientId);
    }
}
