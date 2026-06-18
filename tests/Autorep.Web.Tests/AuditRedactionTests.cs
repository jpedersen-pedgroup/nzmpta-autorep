using Autorep.Web.Data;
using Autorep.Web.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Autorep.Web.Tests;

// Guards the PII fix-gate: the AuditInterceptor must never persist secrets or bulk PII into the
// 7-year-retained AuditEntries.
public class AuditRedactionTests : IClassFixture<AuthedWebAppFactory>
{
    private readonly AuthedWebAppFactory _factory;
    public AuditRedactionTests(AuthedWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Identity_secrets_and_payload_are_redacted_in_audit_entries()
    {
        const string secretHash = "SUPER-SECRET-PASSWORD-HASH";
        const string secretStamp = "SUPER-SECRET-SECURITY-STAMP";
        const string payloadPii = "John Doe, 027-555-0100, john@farm.example";

        string testerId = "audit-" + Guid.NewGuid().ToString("N");
        Guid testId;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();

        db.Users.Add(new Tester
        {
            Id = testerId,
            UserName = testerId + "@test.local",
            Email = testerId + "@test.local",
            PasswordHash = secretHash,
            SecurityStamp = secretStamp,
        });
        await db.SaveChangesAsync();

        var farm = new Farm { Name = "Audit Farm" };
        db.Farms.Add(farm);
        await db.SaveChangesAsync();

        var test = new MachineTest
        {
            TesterId = testerId,
            FarmId = farm.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            PayloadJson = $"{{\"farmer\":\"{payloadPii}\"}}",
        };
        db.MachineTests.Add(test);
        await db.SaveChangesAsync();
        testId = test.Id;

        var testerAudit = await db.AuditEntries
            .FirstAsync(e => e.EntityType == nameof(Tester) && e.EntityKey == testerId);
        testerAudit.AfterJson.Should().NotContain(secretHash);
        testerAudit.AfterJson.Should().NotContain(secretStamp);
        testerAudit.AfterJson.Should().Contain("***REDACTED***");

        var testAudit = await db.AuditEntries
            .FirstAsync(e => e.EntityType == nameof(MachineTest) && e.EntityKey == testId.ToString());
        testAudit.AfterJson.Should().NotContain(payloadPii);
        testAudit.AfterJson.Should().Contain("PayloadJson redacted");
    }
}
