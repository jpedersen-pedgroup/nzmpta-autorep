using Autorep.Web.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Autorep.Web.Data;

public class AutorepDbContext : IdentityDbContext<Tester, IdentityRole, string>
{
    public AutorepDbContext(DbContextOptions<AutorepDbContext> options) : base(options) { }

    public DbSet<TestingCompany> TestingCompanies => Set<TestingCompany>();
    public DbSet<Farm> Farms => Set<Farm>();
    public DbSet<MachineTest> MachineTests => Set<MachineTest>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<MachineTest>()
            .HasIndex(t => new { t.TesterId, t.CreatedAt });

        builder.Entity<MachineTest>()
            .HasIndex(t => t.ClientId)
            .IsUnique()
            .HasFilter("[ClientId] IS NOT NULL");

        builder.Entity<AuditEntry>()
            .HasIndex(a => a.Timestamp);

        builder.Entity<AuditEntry>()
            .HasIndex(a => new { a.EntityType, a.EntityKey });

        builder.Entity<TestingCompany>()
            .HasIndex(c => c.Name)
            .IsUnique();
    }
}
