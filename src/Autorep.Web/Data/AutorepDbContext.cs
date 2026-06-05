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
    public DbSet<Region> Regions => Set<Region>();
    public DbSet<MilkSupplyCompany> MilkSupplyCompanies => Set<MilkSupplyCompany>();
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

        builder.Entity<Farm>(farm =>
        {
            farm.Property(f => f.Name).HasMaxLength(200).IsRequired();
            farm.Property(f => f.SupplyNumber).HasMaxLength(50);
            farm.Property(f => f.AddressLine1).HasMaxLength(200);
            farm.Property(f => f.AddressLine2).HasMaxLength(200);
            farm.Property(f => f.Town).HasMaxLength(100);
            farm.Property(f => f.PostCode).HasMaxLength(10);
            farm.Property(f => f.RapidNumber).HasMaxLength(50);
            farm.Property(f => f.FarmerName).HasMaxLength(200);
            farm.Property(f => f.ContactPhone).HasMaxLength(50);
            farm.Property(f => f.ContactEmail).HasMaxLength(256);
            farm.Property(f => f.Notes).HasMaxLength(2000);

            farm.HasIndex(f => f.Name);
            farm.HasIndex(f => f.SupplyNumber);
            farm.HasIndex(f => f.IsActive);

            // Reference-data lookups (nullable). Restrict so a lookup row that is in
            // use by a Farm can't be deleted out from under it.
            farm.HasOne(f => f.Region)
                .WithMany()
                .HasForeignKey(f => f.RegionId)
                .OnDelete(DeleteBehavior.Restrict);

            farm.HasOne(f => f.MilkSupplyCompany)
                .WithMany()
                .HasForeignKey(f => f.MilkSupplyCompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            // A Farm is referenced by many Machine Tests; don't cascade-delete tests
            // (and their audit history) if a Farm is removed.
            farm.HasMany(f => f.MachineTests)
                .WithOne(t => t.Farm!)
                .HasForeignKey(t => t.FarmId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Region>(region =>
        {
            region.Property(r => r.Name).HasMaxLength(100).IsRequired();
            region.HasIndex(r => r.Name).IsUnique();
        });

        builder.Entity<MilkSupplyCompany>(company =>
        {
            company.Property(c => c.Name).HasMaxLength(100).IsRequired();
            company.HasIndex(c => c.Name).IsUnique();
        });
    }
}
