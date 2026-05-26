using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Autorep.Web.Services;

// Issues short-lived JWT access tokens for the sync API. Refresh tokens are
// handled separately by RefreshTokenService (HMAC + DB-backed rotation).
public class JwtTokenService
{
    private readonly JwtSettings _settings;
    private readonly UserManager<Tester> _users;

    public JwtTokenService(IOptions<JwtSettings> settings, UserManager<Tester> users)
    {
        _settings = settings.Value;
        if (string.IsNullOrWhiteSpace(_settings.SigningKey))
            throw new InvalidOperationException("Jwt:SigningKey is not configured.");
        _users = users;
    }

    public async Task<string> IssueAccessTokenAsync(Tester user)
    {
        var roles = await _users.GetRolesAsync(user);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.UserName ?? string.Empty)
        };
        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_settings.AccessTokenMinutes),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
