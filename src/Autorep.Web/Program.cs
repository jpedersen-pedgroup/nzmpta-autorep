using System.Text;
using Azure.Identity;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Autorep.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Production loads its secrets (JWT signing key, sending mailbox, etc.) from Azure Key Vault via the
// App Service managed identity. The staging/dev vaults are private-network-only and can be
// unreachable from a given host; the Key Vault config provider loads synchronously at startup, so an
// unreachable vault HANGS the whole container until App Service kills it (ContainerTimeout → 503)
// rather than throwing cleanly. To keep non-production resilient we only wire the vault in Production
// (where it's required and reachable) and let other environments fall back to the graceful defaults
// below. ManagedIdentityCredential (not DefaultAzureCredential) gives a fast, deterministic token
// path in Azure. Local development keeps using user-secrets/appsettings (vault left unwired).
var isProduction = builder.Environment.IsProduction();
var keyVaultUri = builder.Configuration["AzureKeyVault:VaultUri"];
if (!string.IsNullOrWhiteSpace(keyVaultUri) && isProduction)
{
    builder.Configuration.AddAzureKeyVault(new Uri(keyVaultUri), new ManagedIdentityCredential());
}

// HttpContextAccessor is needed by the AuditInterceptor to discover the
// current user as the actor on audit entries.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuditInterceptor>();

builder.Services.AddDbContext<AutorepDbContext>((sp, options) =>
{
    // In the Testing environment the integration-test factory supplies the provider
    // (InMemory); everywhere else use SQL Server.
    if (!builder.Environment.IsEnvironment("Testing"))
    {
        var connection = builder.Configuration.GetConnectionString("SqlDatabase")
            ?? throw new InvalidOperationException("ConnectionStrings:SqlDatabase is not configured.");
        options.UseSqlServer(connection);
    }
    options.AddInterceptors(sp.GetRequiredService<AuditInterceptor>());
});

builder.Services
    .AddIdentity<Tester, IdentityRole>(opts =>
    {
        opts.Password.RequireDigit = true;
        opts.Password.RequireLowercase = true;
        opts.Password.RequireUppercase = true;
        opts.Password.RequireNonAlphanumeric = false;
        opts.Password.RequiredLength = 12;
        opts.Lockout.MaxFailedAccessAttempts = 5;
        opts.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        opts.User.RequireUniqueEmail = true;
        opts.SignIn.RequireConfirmedAccount = false;
        // Allow Tester self-enrolment in 2FA via authenticator app.
        opts.Tokens.AuthenticatorTokenProvider = TokenOptions.DefaultAuthenticatorProvider;
    })
    .AddEntityFrameworkStores<AutorepDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(opts =>
{
    opts.LoginPath = "/Account/Login";
    opts.AccessDeniedPath = "/Account/AccessDenied";
    opts.ExpireTimeSpan = TimeSpan.FromHours(8);
    opts.SlidingExpiration = true;
    opts.Cookie.HttpOnly = true;
    opts.Cookie.SecurePolicy = CookieSecurePolicy.Always;

    // API calls must fail with a status code, never a redirect to the login page. `fetch`
    // follows redirects by default and turns a POST into a GET, so an expired cookie would
    // otherwise land the sync push on a 200 HTML login page — which reads as success and marks
    // the tester's test uploaded when the server never received it. Pages keep redirecting.
    opts.Events.OnRedirectToLogin = ctx => ApiAwareChallenge(ctx, StatusCodes.Status401Unauthorized);
    opts.Events.OnRedirectToAccessDenied = ctx => ApiAwareChallenge(ctx, StatusCodes.Status403Forbidden);

    static Task ApiAwareChallenge(
        Microsoft.AspNetCore.Authentication.RedirectContext<CookieAuthenticationOptions> ctx,
        int statusCode)
    {
        if (ctx.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Response.StatusCode = statusCode;
            return Task.CompletedTask;
        }
        ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    }
});

// Email sender: Graph when a sending mailbox is configured; otherwise the log-only sender is
// permitted outside Production. In Production a missing mailbox is a hard startup error rather than
// silently falling back to a sender that would write password-reset links to the logs. (Staging has
// no mail transport provisioned, so it legitimately uses the log-only sender.)
if (!string.IsNullOrWhiteSpace(builder.Configuration["Graph:SendingMailbox"]))
{
    builder.Services.AddSingleton<IEmailSender, GraphEmailSender>();
}
else if (!isProduction)
{
    builder.Services.AddSingleton<IEmailSender, LoggingEmailSender>();
}
else
{
    throw new InvalidOperationException(
        "No email transport configured: set Graph:SendingMailbox (via Key Vault) in Production.");
}

// Emails Company Administrators when a tester sets up a farm in the field (review flow).
builder.Services.AddScoped<FarmReviewNotifier>();

// JWT for the sync API (sits alongside cookie auth used by Razor Pages).
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<RefreshTokenService>();

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtSettings>() ?? new JwtSettings();

// In Production the signing key must come from configuration/Key Vault — never a source-checked-in
// placeholder (which would let anyone mint valid bearer tokens). Fail fast there; outside Production
// (dev/test/staging) fall back to a non-production placeholder so the app can boot without a vault.
var allowDevJwt = !isProduction;
if (!allowDevJwt && string.IsNullOrWhiteSpace(jwt.SigningKey))
    throw new InvalidOperationException("Jwt:SigningKey must be supplied (e.g. via Key Vault) in Production.");
var jwtSigningKey = string.IsNullOrWhiteSpace(jwt.SigningKey)
    ? "dev-only-insecure-signing-key-not-for-production-use!" // reachable only outside Production (guarded above)
    : jwt.SigningKey;

builder.Services.AddAuthentication()
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, opts =>
    {
        opts.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        opts.SaveToken = true;
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization(opts =>
{
    opts.AddPolicy("TesterArea", p => p.RequireRole(Roles.Tester));
    opts.AddPolicy("AdminArea",
        p => p.RequireRole(Roles.SuperAdministrator, Roles.CompanyAdministrator));
    // Super-Administrator-only surfaces.
    opts.AddPolicy("SuperAdminOnly", p => p.RequireRole(Roles.SuperAdministrator));
});

builder.Services.AddRazorPages(opts =>
{
    opts.Conventions.AuthorizeFolder("/App", "TesterArea");
    opts.Conventions.AuthorizeFolder("/Admin", "AdminArea");
    // /Admin/Testers, /Admin/Companies and reference-data management — Super-Admin only.
    // /Admin/Farms stays AdminArea so Company Administrators can edit their own farms (scoped in-page).
    opts.Conventions.AuthorizeFolder("/Admin/Testers", "SuperAdminOnly");
    opts.Conventions.AuthorizeFolder("/Admin/Companies", "SuperAdminOnly");
    opts.Conventions.AuthorizeFolder("/Admin/Regions", "SuperAdminOnly");
    opts.Conventions.AuthorizeFolder("/Admin/MilkSupplyCompanies", "SuperAdminOnly");
    opts.Conventions.AuthorizeFolder("/Admin/Standards", "SuperAdminOnly");
    opts.Conventions.AuthorizeFolder("/Admin/Equipment", "SuperAdminOnly");
    opts.Conventions.AuthorizeFolder("/Admin/FaultObservations", "SuperAdminOnly");
    opts.Conventions.AuthorizeFolder("/Admin/Privacy", "SuperAdminOnly");
});

builder.Services.AddControllers();
builder.Services.AddHealthChecks();

// NZ Post keyless address autocomplete (admin Farm Details screens; online only).
builder.Services.AddHttpClient<NzPostAddressClient>();

var app = builder.Build();

// Apply pending migrations + seed roles on startup. Single-instance fine;
// for multi-instance, gate this on a leader-elect lock or move to release pipeline.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
    // Relational providers (SQL Server) apply migrations; the InMemory provider used in
    // integration tests can't migrate, so create the store directly.
    if (db.Database.IsRelational())
        await db.Database.MigrateAsync();
    else
        await db.Database.EnsureCreatedAsync();
    // E2E tests seed their own data once (SeedOnStartup=false); everything else seeds here.
    if (builder.Configuration.GetValue("SeedOnStartup", true))
    {
        await Seed.RolesAsync(scope.ServiceProvider);
        await Seed.ReferenceDataAsync(scope.ServiceProvider);
        await Seed.BootstrapAdminAsync(scope.ServiceProvider);
        if (app.Environment.IsDevelopment())
        {
            await Seed.DevUsersAsync(scope.ServiceProvider);
        }
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

// Exposed for WebApplicationFactory in integration tests.
public partial class Program { }
