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

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
