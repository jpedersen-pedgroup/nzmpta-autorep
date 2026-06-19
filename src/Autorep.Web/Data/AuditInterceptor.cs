using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Autorep.Web.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Autorep.Web.Data;

// EF Core SaveChangesInterceptor that emits an AuditEntry row for every
// inserted/updated/deleted entity (except AuditEntry itself, to prevent
// recursion). Adds the audit rows to the same SaveChanges batch as the
// originating change, so audit is committed atomically with the change
// it describes. If the parent transaction rolls back, the audit row
// rolls back with it.
public class AuditInterceptor : SaveChangesInterceptor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuditInterceptor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        AddAuditEntries(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        AddAuditEntries(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    private void AddAuditEntries(DbContext? context)
    {
        if (context is null) return;

        var actor = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
        var entries = context.ChangeTracker.Entries()
            .Where(e => e.Entity is not AuditEntry
                        && (e.State == EntityState.Added || e.State == EntityState.Modified || e.State == EntityState.Deleted))
            .ToList();

        foreach (var entry in entries)
        {
            context.Add(BuildAuditEntry(entry, actor));
        }
    }

    private static AuditEntry BuildAuditEntry(EntityEntry entry, string actor)
    {
        var entityType = entry.Entity.GetType().Name;
        var key = string.Join(",", entry.Properties
            .Where(p => p.Metadata.IsPrimaryKey())
            .Select(p => p.CurrentValue?.ToString() ?? ""));

        string? before = null;
        string? after = null;

        switch (entry.State)
        {
            case EntityState.Added:
                after = Serialize(entry, current: true);
                break;
            case EntityState.Deleted:
                before = Serialize(entry, current: false);
                break;
            case EntityState.Modified:
                before = Serialize(entry, current: false);
                after = Serialize(entry, current: true);
                break;
        }

        return new AuditEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Actor = actor,
            EntityType = entityType,
            EntityKey = key,
            Operation = entry.State.ToString(),
            BeforeJson = before,
            AfterJson = after
        };
    }

    // Property-name fragments whose values are secrets and must never reach the 7-year audit
    // store: Identity password/security fields, refresh-token hashes, 2FA/authenticator secrets.
    // Matched case-insensitively so new Identity columns are caught without a code change.
    private static readonly string[] SensitiveNameFragments =
        { "password", "hash", "stamp", "token", "secret", "securitykey", "recoverycode", "twofactor", "authenticator" };

    private const string Redacted = "***REDACTED***";

    private static bool IsSensitive(string name) =>
        SensitiveNameFragments.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase));

    // Serializes the entity's properties for the audit blob, with redaction:
    //  - secret/security properties are masked (never stored), and
    //  - MachineTest.PayloadJson (bulk farm-owner PII) is reduced to a length + SHA-256 hash so the
    //    audit trail stays tamper-evident without retaining the PII for 7 years.
    private static string Serialize(EntityEntry entry, bool current)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var p in entry.Properties.Where(p => !p.Metadata.IsShadowProperty()))
        {
            var name = p.Metadata.Name;
            var value = current ? p.CurrentValue : p.OriginalValue;

            if (IsSensitive(name)) dict[name] = Redacted;
            else if (name == nameof(Domain.Entities.MachineTest.PayloadJson)) dict[name] = SummarizePayload(value as string);
            else dict[name] = value;
        }
        return JsonSerializer.Serialize(dict);
    }

    private static string? SummarizePayload(string? payload)
    {
        if (string.IsNullOrEmpty(payload)) return payload;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        return $"[PayloadJson redacted: len={payload.Length} sha256={hash}]";
    }
}
