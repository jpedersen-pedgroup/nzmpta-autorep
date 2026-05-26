using System.Security.Claims;
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
                after = SerializeCurrentValues(entry);
                break;
            case EntityState.Deleted:
                before = SerializeOriginalValues(entry);
                break;
            case EntityState.Modified:
                before = SerializeOriginalValues(entry);
                after = SerializeCurrentValues(entry);
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

    private static string SerializeCurrentValues(EntityEntry entry)
    {
        var dict = entry.Properties
            .Where(p => !p.Metadata.IsShadowProperty())
            .ToDictionary(p => p.Metadata.Name, p => p.CurrentValue);
        return JsonSerializer.Serialize(dict);
    }

    private static string SerializeOriginalValues(EntityEntry entry)
    {
        var dict = entry.Properties
            .Where(p => !p.Metadata.IsShadowProperty())
            .ToDictionary(p => p.Metadata.Name, p => p.OriginalValue);
        return JsonSerializer.Serialize(dict);
    }
}
