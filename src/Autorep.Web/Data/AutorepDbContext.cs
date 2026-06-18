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
    public DbSet<MachineConfiguration> MachineConfigurations => Set<MachineConfiguration>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<TestStandard> TestStandards => Set<TestStandard>();
    public DbSet<EquipmentItem> EquipmentItems => Set<EquipmentItem>();
    public DbSet<FaultObservation> FaultObservations => Set<FaultObservation>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<MachineTest>()
            .HasIndex(t => new { t.TesterId, t.CreatedAt });

        builder.Entity<MachineTest>()
            .HasIndex(t => t.ClientId)
            .IsUnique()
            .HasFilter("[ClientId] IS NOT NULL");

        builder.Entity<MachineConfiguration>(cfg =>
        {
            cfg.HasIndex(c => c.MachineTestId).IsUnique();
            cfg.HasOne(c => c.MachineTest)
                .WithOne(t => t.Configuration!)
                .HasForeignKey<MachineConfiguration>(c => c.MachineTestId)
                .OnDelete(DeleteBehavior.Cascade);
            cfg.Property(c => c.PlantType).HasConversion<string>().HasMaxLength(40);
            cfg.Property(c => c.PumpLubrication).HasConversion<string>().HasMaxLength(40);
            cfg.Property(c => c.PulsatorModel).HasMaxLength(150);
            cfg.Property(c => c.ClawModel).HasMaxLength(150);
            cfg.Property(c => c.ShellModel).HasMaxLength(150);
            cfg.Property(c => c.LinerModel).HasMaxLength(150);
            cfg.Property(c => c.MilklineSize).HasMaxLength(50);
            cfg.Property(c => c.LastBmcc).HasMaxLength(100);
        });

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

        builder.Entity<Tester>()
            .Property(u => u.CertificateNo)
            .HasMaxLength(50);

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

        builder.Entity<FaultObservation>(fo =>
        {
            fo.Property(f => f.Category).HasMaxLength(100).IsRequired();
            fo.Property(f => f.Name).HasMaxLength(300).IsRequired();
            fo.Property(f => f.Severity).HasMaxLength(20);
            fo.Property(f => f.Recommendation).HasMaxLength(500);
            fo.HasIndex(f => new { f.Category, f.Name }).IsUnique();
        });

        builder.Entity<EquipmentItem>(eq =>
        {
            eq.Property(e => e.Type).HasMaxLength(40).IsRequired();
            eq.Property(e => e.Name).HasMaxLength(200).IsRequired();
            eq.Property(e => e.Brand).HasMaxLength(100);
            eq.HasIndex(e => new { e.Type, e.Name, e.Brand }).IsUnique();
        });

        builder.Entity<TestStandard>(std =>
        {
            std.Property(s => s.Key).HasMaxLength(100).IsRequired();
            std.Property(s => s.Label).HasMaxLength(200).IsRequired();
            std.Property(s => s.Category).HasMaxLength(100);
            std.Property(s => s.Kind).HasMaxLength(20);
            std.Property(s => s.Unit).HasMaxLength(20);
            std.Property(s => s.SourceRef).HasMaxLength(200);
            std.HasIndex(s => s.Key).IsUnique();
        });

        builder.Entity<Region>(region =>
        {
            region.Property(r => r.Name).HasMaxLength(100).IsRequired();
            region.Property(r => r.Island).HasMaxLength(20);
            region.HasIndex(r => r.Name).IsUnique();
        });

        builder.Entity<MilkSupplyCompany>(company =>
        {
            company.Property(c => c.Name).HasMaxLength(100).IsRequired();
            company.Property(c => c.AddressLine1).HasMaxLength(200);
            company.Property(c => c.AddressLine2).HasMaxLength(200);
            company.Property(c => c.Town).HasMaxLength(100);
            company.Property(c => c.PostCode).HasMaxLength(10);
            company.Property(c => c.Phone).HasMaxLength(50);
            company.Property(c => c.Email).HasMaxLength(256);
            company.Property(c => c.LogoContentType).HasMaxLength(100);
            company.HasIndex(c => c.Name).IsUnique();
        });

        builder.Entity<TestingCompany>(company =>
        {
            company.Property(c => c.AddressLine1).HasMaxLength(200);
            company.Property(c => c.AddressLine2).HasMaxLength(200);
            company.Property(c => c.Town).HasMaxLength(100);
            company.Property(c => c.PostCode).HasMaxLength(10);
            company.Property(c => c.Phone).HasMaxLength(50);
            company.Property(c => c.Email).HasMaxLength(256);
            company.Property(c => c.LogoContentType).HasMaxLength(100);
        });
    }
}
