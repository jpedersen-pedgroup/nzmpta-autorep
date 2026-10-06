using System.Net;
using System.Text.RegularExpressions;
using Autorep.Web.Data;
using Autorep.Web.Domain.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Autorep.Web.Tests;

/// <summary>
/// The real sign-in pipeline (Identity cookies, antiforgery, two-factor) on an InMemory store -
/// unlike <see cref="AuthedWebAppFactory"/>, whose header scheme bypasses all of it. Seeds nothing
/// at startup; tests create the accounts they need. Clients are "browsers": a cookie jar, no
/// automatic redirects (so each hop can be asserted), and an https base address, since every
/// cookie in the app is Secure.
/// </summary>
public class CookieAuthWebAppFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = "autorep-cookie-" + Guid.NewGuid();
    private bool _rolesSeeded;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("SeedOnStartup", "false");
        // JwtTokenService refuses to construct without a key, and AuthController takes it by
        // constructor - so without this, /api/auth/login is a 500 before any check runs.
        builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-not-for-any-real-environment-0123456789");
        builder.ConfigureTestServices(services =>
        {
            services.AddDbContext<AutorepDbContext>(o => o.UseInMemoryDatabase(_dbName));
            services.AddSingleton<CapturingEmailSender>();
            services.AddSingleton<Microsoft.AspNetCore.Identity.UI.Services.IEmailSender>(
                sp => sp.GetRequiredService<CapturingEmailSender>());
            // Re-check the security stamp on every request rather than every 30 minutes, so a
            // test can change the stamp and see the cookies it invalidates fall away at once.
            services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero);
        });
    }

    public HttpClient CreateBrowser() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    });

    /// <summary>An account in the given roles, terms pre-accepted (nothing seeds PrivacyContent
    /// here, so the terms gate is inactive anyway), optionally with an authenticator enrolled.
    /// Returns the account and, when enrolled, its Base32 authenticator key.</summary>
    public async Task<(Tester User, string? AuthenticatorKey)> CreateUserAsync(
        string email, string password, string[] roles, bool twoFactor = false, Action<Tester>? configure = null)
    {
        using var scope = Services.CreateScope();
        var sp = scope.ServiceProvider;
        if (!_rolesSeeded)
        {
            await Seed.RolesAsync(sp);
            _rolesSeeded = true;
        }

        var users = sp.GetRequiredService<UserManager<Tester>>();
        var user = new Tester
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = email.Split('@')[0],
        };
        configure?.Invoke(user);
        (await users.CreateAsync(user, password)).Succeeded.Should().BeTrue();
        await users.AddToRolesAsync(user, roles);

        string? key = null;
        if (twoFactor)
        {
            await users.ResetAuthenticatorKeyAsync(user);
            key = await users.GetAuthenticatorKeyAsync(user);
            await users.SetTwoFactorEnabledAsync(user, true);
        }
        return (user, key);
    }

    /// <summary>Runs <paramref name="action"/> against a fresh copy of the account in its own
    /// scope. The copy matters: a <see cref="Tester"/> loaded by one DbContext handed to a
    /// UserManager on another is "already being tracked" the moment the store attaches it.</summary>
    public async Task<T> WithUserAsync<T>(string userId, Func<UserManager<Tester>, Tester, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Tester>>();
        var fresh = await users.FindByIdAsync(userId) ?? throw new InvalidOperationException("no such user");
        return await action(users, fresh);
    }

    public Task WithUserAsync(string userId, Func<UserManager<Tester>, Tester, Task> action) =>
        WithUserAsync(userId, async (u, t) => { await action(u, t); return 0; });
}

/// <summary>Drives Razor Pages forms the way a browser would: GET the page for its antiforgery
/// token, then POST the fields with it.</summary>
public static class BrowserForms
{
    private static readonly Regex Token = new("__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.Compiled);

    public static async Task<HttpResponseMessage> PostFormAsync(
        this HttpClient browser, string pageUrl, IDictionary<string, string> fields, string? postUrl = null)
    {
        var page = await browser.GetAsync(pageUrl);
        page.StatusCode.Should().Be(HttpStatusCode.OK, $"GET {pageUrl} should render the form");
        var html = await page.Content.ReadAsStringAsync();
        var match = Token.Match(html);
        match.Success.Should().BeTrue($"{pageUrl} should carry an antiforgery token");
        var form = new Dictionary<string, string>(fields) { ["__RequestVerificationToken"] = match.Groups[1].Value };
        return await browser.PostAsync(postUrl ?? pageUrl, new FormUrlEncodedContent(form));
    }

    public static Task<HttpResponseMessage> LoginAsync(this HttpClient browser, string email, string password, bool rememberMe = false) =>
        browser.PostFormAsync("/Account/Login", new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["Input.RememberMe"] = rememberMe ? "true" : "false",
        });

    public static Task<HttpResponseMessage> SubmitCodeAsync(this HttpClient browser, string code, bool trustDevice = false) =>
        browser.PostFormAsync("/Account/TwoFactorChallenge", new Dictionary<string, string>
        {
            ["Input.Code"] = code,
            ["Input.RememberThisDevice"] = trustDevice ? "true" : "false",
        });

    /// <summary>Signs out through the layout's form, which is on any signed-in page.</summary>
    public static Task<HttpResponseMessage> LogoutAsync(this HttpClient browser) =>
        browser.PostFormAsync("/Account/Manage", new Dictionary<string, string>(), postUrl: "/Account/Logout");

    /// <summary>The redirect target as a path and query. Identity's challenge redirect is
    /// absolute (https://localhost/Account/Login?...), the app's own are relative.</summary>
    public static string? Location(this HttpResponseMessage response) =>
        response.Headers.Location is { } l ? (l.IsAbsoluteUri ? l.PathAndQuery : l.ToString()) : null;

    /// <summary>The Set-Cookie header for the named cookie, or null.</summary>
    public static string? SetCookie(this HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(v => v.StartsWith(name + "=", StringComparison.Ordinal))
            : null;
}
