using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Autorep.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Autorep.Web.Api;

// JWT-based auth for the sync API (separate from cookie auth used by Razor Pages).
// Tester PWA hits these from the device:
//   POST /api/auth/login   { email, password }   → access + refresh
//   POST /api/auth/refresh { refreshToken }       → new access + rotated refresh
//   POST /api/auth/logout  { refreshToken }       → revokes the supplied token
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<Tester> _users;
    private readonly JwtTokenService _jwt;
    private readonly RefreshTokenService _refresh;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        UserManager<Tester> users,
        JwtTokenService jwt,
        RefreshTokenService refresh,
        ILogger<AuthController> logger)
    {
        _users = users;
        _jwt = jwt;
        _refresh = refresh;
        _logger = logger;
    }

    public record LoginRequest(string Email, string Password);
    public record RefreshRequest(string RefreshToken);
    public record LogoutRequest(string RefreshToken);
    public record TokenResponse(
        string AccessToken,
        string RefreshToken,
        DateTimeOffset RefreshExpiresAt,
        int AccessTokenLifetimeSeconds);

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        var user = await _users.FindByEmailAsync(req.Email);
        if (user is null) return Unauthorized();

        // Honour lockout.
        if (user.LockoutEnd is { } end && end > DateTimeOffset.UtcNow)
            return StatusCode(StatusCodes.Status423Locked);

        var pwOk = await _users.CheckPasswordAsync(user, req.Password);
        if (!pwOk)
        {
            await _users.AccessFailedAsync(user);
            return Unauthorized();
        }

        // Licence check (pure Testers only — admins can sign in regardless).
        var roles = await _users.GetRolesAsync(user);
        var isPureTester = roles.Contains(Roles.Tester)
            && !roles.Contains(Roles.SuperAdministrator)
            && !roles.Contains(Roles.CompanyAdministrator);
        if (isPureTester && user.LicenceExpiryDate is { } expiry
            && expiry < DateOnly.FromDateTime(DateTime.UtcNow))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new { error = "licence-expired" });
        }

        await _users.ResetAccessFailedCountAsync(user);

        var access = await _jwt.IssueAccessTokenAsync(user);
        var refresh = await _refresh.IssueAsync(user.Id, ct);

        return Ok(new TokenResponse(
            access,
            refresh.RawToken,
            refresh.ExpiresAt,
            60 * 60));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest req, CancellationToken ct)
    {
        var rotated = await _refresh.RotateAsync(req.RefreshToken, ct);
        if (rotated is null) return Unauthorized();

        var user = await _users.FindByIdAsync(rotated.TesterId);
        if (user is null) return Unauthorized();

        // Re-check lockout/licence at every refresh so an admin force-logout
        // or licence expiry takes effect within 1h (the access-token lifetime).
        if (user.LockoutEnd is { } end && end > DateTimeOffset.UtcNow)
            return StatusCode(StatusCodes.Status423Locked);

        var roles = await _users.GetRolesAsync(user);
        var isPureTester = roles.Contains(Roles.Tester)
            && !roles.Contains(Roles.SuperAdministrator)
            && !roles.Contains(Roles.CompanyAdministrator);
        if (isPureTester && user.LicenceExpiryDate is { } expiry
            && expiry < DateOnly.FromDateTime(DateTime.UtcNow))
        {
            await _refresh.RevokeAllAsync(user.Id, "licence-expired");
            return StatusCode(StatusCodes.Status403Forbidden,
                new { error = "licence-expired" });
        }

        var access = await _jwt.IssueAccessTokenAsync(user);
        return Ok(new TokenResponse(
            access,
            rotated.RawToken,
            rotated.ExpiresAt,
            60 * 60));
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest req, CancellationToken ct)
    {
        await _refresh.RevokeAsync(req.RefreshToken, "logout", ct);
        return NoContent();
    }
}
