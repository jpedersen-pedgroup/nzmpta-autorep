namespace Autorep.Web.Domain.Entities;

// Captured by AuditInterceptor for every entity write (insert/update/delete).
// Stored as JSON before/after blobs so the same row schema serves all entity
// types. 7-year retention per PRD §Authorization.
public class AuditEntry
{
    public long Id { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string? Actor { get; set; }              // user id or "system"
    public string EntityType { get; set; } = string.Empty;
    public string EntityKey { get; set; } = string.Empty;   // primary key, as string
    public string Operation { get; set; } = string.Empty;   // Added / Modified / Deleted
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
}
