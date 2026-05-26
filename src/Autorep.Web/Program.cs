using Autorep.Web.Data;
using Autorep.Web.Domain;
using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// HttpContextAccessor is needed by the AuditInterceptor to discover the
// current user as the actor on audit entries.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuditInterceptor>();

builder.Services.AddDbContext<AutorepDbContext>((sp, options) =>
{
    var connection = builder.Configuration.GetConnectionString("SqlDatabase")
        ?? throw new InvalidOperationException("ConnectionStrings:SqlDatabase is not configured.");
    options.UseSqlServer(connection);
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
        // RequireConfirmedAccount = false for Phase 1; flip on in Phase 8 once email is wired.
        opts.SignIn.RequireConfirmedAccount = false;
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

builder.Services.AddAuthorization(opts =>
{
    opts.AddPolicy("TesterArea", p => p.RequireRole(Roles.Tester));
    opts.AddPolicy("AdminArea", p => p.RequireRole(Roles.SuperAdministrator, Roles.CompanyAdministrator));
});

builder.Services.AddRazorPages(opts =>
{
    opts.Conventions.AuthorizeFolder("/App", "TesterArea");
    opts.Conventions.AuthorizeFolder("/Admin", "AdminArea");
});

builder.Services.AddControllers();
builder.Services.AddHealthChecks();

var app = builder.Build();

// Apply pending migrations + seed roles on startup. Single-instance fine;
// for multi-instance, gate this on a leader-elect lock or move to release pipeline.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AutorepDbContext>();
    await db.Database.MigrateAsync();
    await Seed.RolesAsync(scope.ServiceProvider);
    if (app.Environment.IsDevelopment())
    {
        await Seed.DevUsersAsync(scope.ServiceProvider);
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
