using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autorep.Web.Tests;

// Integration-test authentication: a request authenticates as the role(s) in the
// "X-Test-Role" header (comma-separated), with "X-Test-User" as the user id. No header =
// anonymous (NoResult), so unauthenticated paths still behave normally.
public class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string UserHeader = "X-Test-User";
    public const string RoleHeader = "X-Test-Role";
    /// <summary>Extra "type=value" claims, semicolon-separated — e.g. the sync-only licence scope.</summary>
    public const string ClaimsHeader = "X-Test-Claims";
    /// <summary>Round-trip timestamp for the session's last second-factor proof, stored in the
    /// ticket's properties under <c>MfaPolicy.SessionStampKey</c> as TesterSignInManager does.</summary>
    public const string MfaAtHeader = "X-Test-MfaAt";
    /// <summary>Value of <see cref="MfaAtHeader"/> meaning "no stamp in the ticket at all".</summary>
    public const string NoMfaStamp = "none";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(RoleHeader, out var roles) || roles.Count == 0)
            return Task.FromResult(AuthenticateResult.NoResult());

        var userId = Request.Headers.TryGetValue(UserHeader, out var u) && !string.IsNullOrEmpty(u)
            ? u.ToString()
            : "test-user";

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, userId),
        };
        foreach (var role in roles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            claims.Add(new Claim(ClaimTypes.Role, role));

        if (Request.Headers.TryGetValue(ClaimsHeader, out var extra))
        {
            foreach (var pair in extra.ToString().Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = pair.Split('=', 2);
                if (parts.Length == 2) claims.Add(new Claim(parts[0], parts[1]));
            }
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        // Second-factor stamp: a Super-Administrator ticket without one is treated as stale and
        // signed out, so the default is "proved just now" - the state of any real admin session
        // that reached a page. "none" sends no stamp (a ticket from before stamps existed).
        var properties = new AuthenticationProperties();
        var mfaAt = Request.Headers.TryGetValue(MfaAtHeader, out var h) && !string.IsNullOrEmpty(h)
            ? h.ToString()
            : DateTimeOffset.UtcNow.ToString("o");
        if (mfaAt != NoMfaStamp)
            properties.Items[Autorep.Web.Domain.MfaPolicy.SessionStampKey] = mfaAt;
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, properties, SchemeName)));
    }
}
