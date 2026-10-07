using System.Text.Json.Nodes;
using Autorep.Web.Data;
using Autorep.Web.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Autorep.Web.Tests;

/// <summary>
/// Shared arrangements for the version tests: a company with a farm, its people, and completed tests
/// carrying a payload shaped like the device's LocalTest — what the wizard, the merge and the
/// admin edit all work on.
/// </summary>
internal static class TestPayloads
{
    public const string Completed = "2026-09-01T01:00:00.000Z";

    /// <summary>A completed version-1 capture: a fault with its recommendation, a couple of readings,
    /// general comments, a check-all attestation and the sign-off.</summary>
    public static JsonObject Original(Guid clientId, string farmName = "Kowhai Flats") => new()
    {
        ["id"] = clientId.ToString(),
        ["farmId"] = null,
        ["farmName"] = farmName,
        ["farm"] = new JsonObject { ["name"] = farmName, ["supplyNumber"] = "40123", ["farmerName"] = "Aroha Farmer" },
        ["config"] = new JsonObject
        {
            ["plantType"] = "Rotary", ["clusterCount"] = 20, ["flushingPulsationSystem"] = false,
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
            ["vp.oilWater"] = new JsonObject { ["status"] = "ok" },
        },
        ["attestations"] = new JsonArray
        {
            new JsonObject { ["step"] = "VisualFaultsPreStart", ["section"] = "vacuumPump", ["attestedAt"] = Completed, ["text"] = "I have inspected all items in this section." },
            new JsonObject { ["step"] = "ReviewSignOff", ["attestedAt"] = Completed, ["text"] = "I confirm this test has been completed and the results are accurate." },
        },
        ["readings"] = new JsonObject { ["tr.workingVacuum"] = 48, ["tr.nominalVacuum"] = 50, ["tr.regulationDeviation"] = -2 },
        ["recommendations"] = new JsonObject { ["vp.wick"] = "Clean the wicks" },
        ["dataFields"] = new JsonObject(),
        ["notes"] = "Original comment",
        ["nextTestDate"] = "2027-09-01",
        ["testedBy"] = new JsonObject { ["name"] = "Ellie Tester" },
        ["createdAt"] = "2026-09-01T00:00:00.000Z",
        ["updatedAt"] = Completed,
        ["markedCompleteAt"] = Completed,
        ["syncState"] = "uploaded",
        ["version"] = 1,
    };

    /// <summary>An edit of <paramref name="basePayload"/> as the browser sends it: the change applied,
    /// and one amendment record appended to the history it carried.</summary>
    public static JsonObject Edited(JsonObject basePayload, Action<JsonObject> change, int version = 2)
    {
        var edited = (JsonObject)basePayload.DeepClone();
        change(edited);
        var history = edited["amendments"] as JsonArray ?? new JsonArray();
        history.Add(new JsonObject
        {
            ["version"] = version,
            ["amendedAt"] = "2026-10-07T00:00:00.000Z",
            ["baseVersion"] = version - 1,
            ["changes"] = new JsonArray
            {
                new JsonObject { ["section"] = "Other", ["label"] = "General comments", ["from"] = "a", ["to"] = "b" },
            },
        });
        edited["amendments"] = history;
        return edited;
    }

    /// <summary>A Testing Company with a farm.</summary>
    public static async Task<(Guid CompanyId, Guid FarmId)> SeedCompanyAsync(IServiceProvider services, string name)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
        var company = new TestingCompany { Name = $"{name} {Guid.NewGuid()}" };
        db.TestingCompanies.Add(company);
        var farm = new Farm { Name = $"{name} Farm", CreatedByTestingCompanyId = company.Id };
        db.Farms.Add(farm);
        await db.SaveChangesAsync();
        return (company.Id, farm.Id);
    }

    public static async Task SeedUserAsync(IServiceProvider services, string id, Guid? companyId, string? displayName = null)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
        db.Users.Add(new Tester
        {
            Id = id,
            UserName = $"{id}@local",
            Email = $"{id}@local",
            DisplayName = displayName ?? id,
            TestingCompanyId = companyId,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>A test row; returns it as stored.</summary>
    public static async Task<MachineTest> SeedTestAsync(
        IServiceProvider services, string testerId, Guid farmId, Guid? companyId, JsonObject? payload,
        bool complete = true, int version = 1, Guid? clientId = null, Guid? supersedes = null, Guid? root = null)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
        var id = clientId ?? Guid.NewGuid();
        var test = new MachineTest
        {
            ClientId = id,
            TesterId = testerId,
            FarmId = farmId,
            TestingCompanyId = companyId,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            MarkedCompleteAt = complete ? DateTimeOffset.UtcNow.AddDays(-30) : null,
            Version = version,
            SupersedesClientId = supersedes,
            RootClientId = root ?? (supersedes is null ? id : null),
            PayloadJson = payload?.ToJsonString(),
            Notes = payload?["notes"]?.GetValue<string>(),
            Configuration = new MachineConfiguration { ClusterCount = 20, PlantType = PlantType.Rotary, PulsatorCount = 10 },
        };
        db.MachineTests.Add(test);
        await db.SaveChangesAsync();
        return test;
    }
}
