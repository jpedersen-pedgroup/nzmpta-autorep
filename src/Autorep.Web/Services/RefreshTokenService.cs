using System.Security.Cryptography;
using System.Text;
using Autorep.Web.Data;
using Autorep.Web.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Autorep.Web.Services;

// Issues and rotates DB-backed refresh tokens for the sync API. Raw tokens
// are never persisted — only the SHA-256 hash. Rotation: every refresh issues
// a new token and revokes the old one ("rotated"). Replay of a revoked token
// triggers a chain-wide revoke ("replay") and forces full re-authentication.
public class RefreshTokenService
{
    private readonly AutorepDbContext _db;
    private readonly JwtSettings _settings;
    private const int TokenBytes = 32;

    public RefreshTokenService(AutorepDbContext db, IOptions<JwtSettings> settings)
    {
        _db = db;
        _settings = settings.Value;
    }

    public record IssuedToken(string RawToken, DateTimeOffset ExpiresAt, string TesterId);

    public async Task<IssuedToken> IssueAsync(string testerId, CancellationToken ct = default)
    {
        var raw = GenerateRawToken();
        var hash = Hash(raw);
        var expires = DateTimeOffset.UtcNow.AddDays(_settings.RefreshTokenDays);

        var entity = new RefreshToken
        {
            TesterId = testerId,
            TokenHash = hash,
            ExpiresAt = expires
        };
        _db.RefreshTokens.Add(entity);
        await _db.SaveChangesAsync(ct);

        return new IssuedToken(raw, expires, testerId);
    }

    // Returns the new token on success, null on invalid/expired/replay.
    public async Task<IssuedToken?> RotateAsync(string rawToken, CancellationToken ct = default)
    {
        var hash = Hash(rawToken);
        var existing = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (existing is null) return null;

        if (!existing.IsActive)
        {
            // Replay detection: revoke ALL active tokens for this Tester.
            if (existing.RevokedAt is not null && existing.RevokedReason != "rotated")
            {
                // already handled
            }
            else if (existing.RevokedAt is not null)
            {
                var allActive = await _db.RefreshTokens
                    .Where(t => t.TesterId == existing.TesterId && t.RevokedAt == null)
                    .ToListAsync(ct);
                foreach (var t in allActive)
                {
                    t.RevokedAt = DateTimeOffset.UtcNow;
                    t.RevokedReason = "replay";
                }
                await _db.SaveChangesAsync(ct);
            }
            return null;
        }

        // Issue new + revoke old, with chain link.
        var newToken = await IssueAsync(existing.TesterId, ct);
        existing.RevokedAt = DateTimeOffset.UtcNow;
        existing.RevokedReason = "rotated";
        var newHash = Hash(newToken.RawToken);
        var newRow = await _db.RefreshTokens.FirstAsync(t => t.TokenHash == newHash, ct);
        existing.ReplacedById = newRow.Id;
        await _db.SaveChangesAsync(ct);

        return newToken;
    }

    public async Task RevokeAllAsync(string testerId, string reason, CancellationToken ct = default)
    {
        var active = await _db.RefreshTokens
            .Where(t => t.TesterId == testerId && t.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var t in active)
        {
            t.RevokedAt = DateTimeOffset.UtcNow;
            t.RevokedReason = reason;
        }
        if (active.Count > 0)
            await _db.SaveChangesAsync(ct);
    }

    public async Task RevokeAsync(string rawToken, string reason, CancellationToken ct = default)
    {
        var hash = Hash(rawToken);
        var row = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (row is null || row.RevokedAt is not null) return;
        row.RevokedAt = DateTimeOffset.UtcNow;
        row.RevokedReason = reason;
        await _db.SaveChangesAsync(ct);
    }

    private static string GenerateRawToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenBytes);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string Hash(string raw)
    {
        var bytes = Encoding.UTF8.GetBytes(raw);
        var hashed = SHA256.HashData(bytes);
        return Convert.ToHexString(hashed);
    }
}
