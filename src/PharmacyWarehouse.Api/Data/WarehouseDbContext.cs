using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Stock;
using PharmacyWarehouse.Domain.Storage;

namespace PharmacyWarehouse.Api.Data;

public class WarehouseDbContext : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>
{
    public WarehouseDbContext(DbContextOptions<WarehouseDbContext> options) : base(options)
    {
    }

    public DbSet<StorageLocation> StorageLocations => Set<StorageLocation>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductBarcode> ProductBarcodes => Set<ProductBarcode>();
    public DbSet<Batch> Batches => Set<Batch>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<Withdrawal> Withdrawals => Set<Withdrawal>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
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

        builder.Entity<Product>(product =>
        {
            product.Property(p => p.Name).IsRequired().HasMaxLength(ProductRules.MaxName);
            product.Property(p => p.NameKey).IsRequired().HasMaxLength(ProductRules.MaxName);
            product.Property(p => p.Brand).HasMaxLength(ProductRules.MaxBrand);
            product.Property(p => p.BrandKey).IsRequired().HasMaxLength(ProductRules.MaxBrand);
            product.Property(p => p.PackDescription).HasMaxLength(ProductRules.MaxPack);
            product.Property(p => p.PackKey).IsRequired().HasMaxLength(ProductRules.MaxPack);
            product.Property(p => p.SearchText).IsRequired();
            product.Property(p => p.Version).IsConcurrencyToken();
            product.HasOne<StorageLocation>().WithMany().HasForeignKey(p => p.LocationId).OnDelete(DeleteBehavior.Restrict);
            product.HasIndex(p => new { p.NameKey, p.BrandKey, p.PackKey }).IsUnique();
        });

        builder.Entity<ProductBarcode>(barcode =>
        {
            barcode.Property(b => b.Code).IsRequired().HasMaxLength(ProductRules.MaxCode);
            barcode.Property(b => b.CodeKey).IsRequired().HasMaxLength(ProductRules.MaxCode);
            barcode.Property(b => b.Version).IsConcurrencyToken();
            barcode.HasOne<Product>().WithMany().HasForeignKey(b => b.ProductId).OnDelete(DeleteBehavior.Restrict);
            barcode.HasIndex(b => b.CodeKey).IsUnique();
        });

        builder.Entity<Batch>(batch =>
        {
            batch.ToTable(t => t.HasCheckConstraint("CK_Batches_OnHand", "OnHand >= 0"));
            batch.Property(b => b.BatchNumber).IsRequired().HasMaxLength(ProductRules.MaxBatch);
            batch.Property(b => b.BatchKey).IsRequired().HasMaxLength(ProductRules.MaxBatch);
            batch.Property(b => b.ExpiryPrecision).HasConversion<string>().HasMaxLength(10);
            batch.Property(b => b.ExpiryCalendar).HasConversion<string>().HasMaxLength(10);
            batch.Property(b => b.Version).IsConcurrencyToken();
            batch.HasOne<Product>().WithMany().HasForeignKey(b => b.ProductId).OnDelete(DeleteBehavior.Restrict);
            batch.HasOne<StorageLocation>().WithMany().HasForeignKey(b => b.LocationId).OnDelete(DeleteBehavior.Restrict);
            batch.HasIndex(b => new { b.ProductId, b.BatchKey }).IsUnique();
            batch.HasIndex(b => b.LocationId);
        });

        builder.Entity<StockMovement>(movement =>
        {
            movement.Property(m => m.Kind).HasConversion<string>().HasMaxLength(20);
            movement.Property(m => m.Note).HasMaxLength(ProductRules.MaxNote);
            movement.HasOne<Batch>().WithMany().HasForeignKey(m => m.BatchId).OnDelete(DeleteBehavior.Restrict);
            movement.HasOne<StockMovement>().WithMany().HasForeignKey(m => m.ReversesId).OnDelete(DeleteBehavior.Restrict);
            movement.HasIndex(m => new { m.BatchId, m.AtUtc });
        });

        builder.Entity<Withdrawal>(withdrawal =>
        {
            withdrawal.Property(w => w.Status).HasConversion<string>().HasMaxLength(10);
            withdrawal.Property(w => w.ReviewNote).HasMaxLength(Withdrawal.MaxReviewNote);
            withdrawal.Property(w => w.Version).IsConcurrencyToken();
            withdrawal.HasOne<Product>().WithMany().HasForeignKey(w => w.ProductId).OnDelete(DeleteBehavior.Restrict);
            withdrawal.HasIndex(w => new { w.UserId, w.OccurredAtUtc });
            withdrawal.HasIndex(w => new { w.Status, w.AtUtc });
        });

        builder.Entity<StockMovement>().HasOne<Withdrawal>().WithMany().HasForeignKey(m => m.WithdrawalId).OnDelete(DeleteBehavior.Restrict);

        builder.Entity<AppSetting>(setting =>
        {
            setting.Property(s => s.Key).IsRequired().HasMaxLength(AppSetting.MaxKey);
            setting.Property(s => s.Value).IsRequired().HasMaxLength(AppSetting.MaxValue);
            setting.Property(s => s.Version).IsConcurrencyToken();
            setting.HasIndex(s => s.Key).IsUnique();
            setting.HasData(
                new AppSetting { Id = new Guid("6f3b1c52-0a11-4d7e-9c10-3a5e7d210001"), Key = DashboardRules.NearMonthsKey, Value = "3" },
                new AppSetting { Id = new Guid("6f3b1c52-0a11-4d7e-9c10-3a5e7d210002"), Key = DashboardRules.FarMonthsKey, Value = "6" });
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
