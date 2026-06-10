using System.Text;
using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Autorep.Web.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

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
});

// Email sender: pick Graph if SendingMailbox configured, else log-only.
if (!string.IsNullOrWhiteSpace(builder.Configuration["Graph:SendingMailbox"]))
{
    builder.Services.AddSingleton<IEmailSender, GraphEmailSender>();
}
else
{
    builder.Services.AddSingleton<IEmailSender, LoggingEmailSender>();
}

// JWT for the sync API (sits alongside cookie auth used by Razor Pages).
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<RefreshTokenService>();

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtSettings>() ?? new JwtSettings();
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
            IssuerSigningKey = string.IsNullOrWhiteSpace(jwt.SigningKey)
                ? new SymmetricSecurityKey(Encoding.UTF8.GetBytes("dev-only-placeholder-signing-key-please-override-32"))
                : new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
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
