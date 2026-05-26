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
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

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

        builder.Entity<RefreshToken>()
            .HasIndex(t => t.TokenHash)
            .IsUnique();

        builder.Entity<RefreshToken>()
            .HasIndex(t => new { t.TesterId, t.RevokedAt });

        builder.Entity<RefreshToken>()
            .HasOne(t => t.Tester)
            .WithMany()
            .HasForeignKey(t => t.TesterId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Tester>()
            .HasIndex(u => u.TestingCompanyId);
    }
}
