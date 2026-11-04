using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Storage;

namespace PharmacyWarehouse.Api.Data;

public class WarehouseDbContext : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>
{
    public WarehouseDbContext(DbContextOptions<WarehouseDbContext> options) : base(options)
    {
    }

    public DbSet<StorageLocation> StorageLocations => Set<StorageLocation>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<StorageLocation>(location =>
        {
            location.Property(l => l.Name).IsRequired().HasMaxLength(LocationTree.MaxName);
            location.Property(l => l.NameKey).IsRequired().HasMaxLength(LocationTree.MaxName);
            location.Property(l => l.ParentKey).IsRequired().HasMaxLength(36);
            location.Property(l => l.Version).IsConcurrencyToken();
            location.HasOne<StorageLocation>().WithMany().HasForeignKey(l => l.ParentId).OnDelete(DeleteBehavior.Restrict);
            location.HasIndex(l => new { l.ParentKey, l.NameKey }).IsUnique();
        });

        builder.Entity<AuditEntry>(entry =>
        {
            entry.Property(a => a.UserName).IsRequired().HasMaxLength(256);
            entry.Property(a => a.EntityType).IsRequired().HasMaxLength(20);
            entry.Property(a => a.EntityLabel).HasMaxLength(50);
            entry.Property(a => a.Action).IsRequired().HasMaxLength(20);
            entry.Property(a => a.Field).HasMaxLength(50);
            entry.Property(a => a.Reason).HasMaxLength(AuditEntry.MaxReason);
            entry.HasIndex(a => new { a.SubjectId, a.AtUtc });
            entry.HasIndex(a => a.EntityId);
        });
    }
}
