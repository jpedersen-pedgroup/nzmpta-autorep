using System.Text.Json;
using Autorep.Web.Data;
using Autorep.Web.Domain.Entities;

namespace Autorep.Web.Services;

/// <summary>
/// The authoritative record of every sign-in attempt and its outcome - password, two-factor
/// code, recovery code - as an <see cref="AuditEntry"/> (covered by the IPP3A notice and the
/// retention policy). The app log gets only the outcome and user id, never the email.
/// </summary>
public class LoginAudit(AutorepDbContext db, ILogger<LoginAudit> logger)
{
    public async Task WriteAsync(HttpContext ctx, string email, string? userId, string outcome)
    {
        logger.LogInformation("Login {Outcome} for user {UserId}", outcome, userId ?? "(anonymous)");
        db.AuditEntries.Add(new AuditEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Actor = userId ?? "anonymous",
            EntityType = "Login",
            EntityKey = email,
            Operation = outcome,
            AfterJson = JsonSerializer.Serialize(new
            {
                ip = ctx.Connection.RemoteIpAddress?.ToString(),
                userAgent = ctx.Request.Headers.UserAgent.ToString(),
            }),
        });
        try { await db.SaveChangesAsync(); }
        catch (Exception ex) { logger.LogWarning(ex, "Failed to write login audit row"); }
    }
}
