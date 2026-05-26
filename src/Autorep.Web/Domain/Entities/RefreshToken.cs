namespace Autorep.Web.Domain.Entities;

// Refresh tokens for the JWT-based sync API. Tokens are stored as SHA-256
// hashes so a DB compromise doesn't leak active sessions. Rotation: every
// refresh issues a new token and revokes the old one; replay of a revoked
// token triggers revocation of the whole chain (RevokedReason = "replay").
public class RefreshToken
{
    public long Id { get; set; }

    public string TesterId { get; set; } = string.Empty;
    public Tester? Tester { get; set; }

    // SHA-256 hash of the raw token. The raw token only ever exists in the
    // response body of /api/auth/login and /api/auth/refresh.
    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset IssuedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }

    // When this token is rotated by a refresh call, points to the row id of
    // the newly-issued token so we can chain back through history.
    public long? ReplacedById { get; set; }

    public bool IsActive => RevokedAt is null && ExpiresAt > DateTimeOffset.UtcNow;
}
