using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PharmacyWarehouse.Domain.Assets;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Api.Data;

public class WarehouseDbContext : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>
{
    public WarehouseDbContext(DbContextOptions<WarehouseDbContext> options) : base(options)
    {
    }

    public DbSet<Building> Buildings => Set<Building>();
    public DbSet<Floor> Floors => Set<Floor>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<Equipment> Equipment => Set<Equipment>();
    public DbSet<Component> Components => Set<Component>();
    public DbSet<RepairRecord> Repairs => Set<RepairRecord>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Building>(building =>
        {
            building.Property(b => b.Name).IsRequired().HasMaxLength(LocationRules.MaxBuildingName);
            building.Property(b => b.NameKey).IsRequired().HasMaxLength(LocationRules.MaxBuildingName);
            building.Property(b => b.Version).IsConcurrencyToken();
            building.HasIndex(b => b.NameKey).IsUnique();
        });

        builder.Entity<Floor>(floor =>
        {
            floor.Property(f => f.Name).IsRequired().HasMaxLength(LocationRules.MaxFloorName);
            floor.Property(f => f.NameKey).IsRequired().HasMaxLength(LocationRules.MaxFloorName);
            floor.Property(f => f.Version).IsConcurrencyToken();
            floor.HasOne<Building>().WithMany().HasForeignKey(f => f.BuildingId).OnDelete(DeleteBehavior.Restrict);
            floor.HasIndex(f => new { f.BuildingId, f.NameKey }).IsUnique();
        });

        builder.Entity<Unit>(unit =>
        {
            unit.Property(u => u.Name).IsRequired().HasMaxLength(LocationRules.MaxUnitName);
            unit.Property(u => u.NameKey).IsRequired().HasMaxLength(LocationRules.MaxUnitName);
            unit.Property(u => u.Version).IsConcurrencyToken();
            unit.HasIndex(u => u.NameKey).IsUnique();
        });

        builder.Entity<Equipment>(equipment =>
        {
            equipment.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);
            equipment.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            equipment.Property(e => e.AssetNumber).HasMaxLength(EquipmentRules.MaxAssetNumber);
            equipment.Property(e => e.AssetNumberKey).HasMaxLength(EquipmentRules.MaxAssetNumber);
            equipment.Property(e => e.SerialNumber).HasMaxLength(EquipmentRules.MaxSerialNumber);
            equipment.Property(e => e.Brand).HasMaxLength(EquipmentRules.MaxBrand);
            equipment.Property(e => e.Model).HasMaxLength(EquipmentRules.MaxModel);
            equipment.Property(e => e.SizeText).HasMaxLength(EquipmentRules.MaxSizeText);
            equipment.Property(e => e.ComputerName).HasMaxLength(EquipmentRules.MaxComputerName);
            equipment.Property(e => e.ComputerNameKey).HasMaxLength(EquipmentRules.MaxComputerName);
            equipment.Property(e => e.PeripheralKind).HasMaxLength(EquipmentRules.MaxPeripheralKind);
            equipment.Property(e => e.MouseModel).HasMaxLength(EquipmentRules.MaxModel);
            equipment.Property(e => e.KeyboardModel).HasMaxLength(EquipmentRules.MaxModel);
            equipment.Property(e => e.OtherAccessories).HasMaxLength(EquipmentRules.MaxOtherAccessories);
            equipment.Property(e => e.Room).HasMaxLength(EquipmentRules.MaxRoom);
            equipment.Property(e => e.Station).HasMaxLength(EquipmentRules.MaxStation);
            equipment.Property(e => e.StationPosition).HasMaxLength(EquipmentRules.MaxStationPosition);
            equipment.Property(e => e.Notes).HasMaxLength(EquipmentRules.MaxNotes);
            equipment.Property(e => e.Version).IsConcurrencyToken();

            equipment.HasOne(e => e.OwnerUnit).WithMany().HasForeignKey(e => e.OwnerUnitId).OnDelete(DeleteBehavior.Restrict);
            equipment.HasOne(e => e.LocationUnit).WithMany().HasForeignKey(e => e.LocationUnitId).OnDelete(DeleteBehavior.Restrict);
            equipment.HasOne(e => e.Building).WithMany().HasForeignKey(e => e.BuildingId).OnDelete(DeleteBehavior.Restrict);
            equipment.HasOne(e => e.Floor).WithMany().HasForeignKey(e => e.FloorId).OnDelete(DeleteBehavior.Restrict);
            equipment.HasOne(e => e.Parent).WithMany(e => e.Children).HasForeignKey(e => e.ParentId).OnDelete(DeleteBehavior.Restrict);

            // آخرین سد در برابر دو ثبت هم‌زمان. چند NULL مجاز است (دستگاه بدون شماره).
            equipment.HasIndex(e => e.AssetNumberKey).IsUnique();
            equipment.HasIndex(e => e.ComputerNameKey);
            equipment.HasIndex(e => e.Status);
            equipment.HasIndex(e => e.Type);
        });

        builder.Entity<Component>(component =>
        {
            component.Property(c => c.Kind).HasConversion<string>().HasMaxLength(20);
            component.Property(c => c.Brand).HasMaxLength(EquipmentRules.MaxBrand);
            component.Property(c => c.Model).HasMaxLength(EquipmentRules.MaxModel);
            component.Property(c => c.Capacity).HasMaxLength(EquipmentRules.MaxCapacity);
            component.Property(c => c.Version).IsConcurrencyToken();
            component.HasOne<Equipment>()
                .WithMany(e => e.Components)
                .HasForeignKey(c => c.EquipmentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<RepairRecord>(repair =>
        {
            repair.Property(r => r.Vendor).HasMaxLength(RepairRules.MaxVendor);
            repair.Property(r => r.Problem).IsRequired().HasMaxLength(RepairRules.MaxProblem);
            repair.Property(r => r.Result).HasMaxLength(RepairRules.MaxResult);
            repair.Property(r => r.Version).IsConcurrencyToken();
            repair.HasOne<Equipment>().WithMany().HasForeignKey(r => r.EquipmentId).OnDelete(DeleteBehavior.Restrict);
            repair.HasIndex(r => new { r.EquipmentId, r.SentAtUtc });
        });

        builder.Entity<AuditEntry>(entry =>
        {
            entry.Property(a => a.UserName).IsRequired().HasMaxLength(256);
            entry.Property(a => a.EntityType).IsRequired().HasMaxLength(20);
            entry.Property(a => a.EntityLabel).HasMaxLength(50);
            entry.Property(a => a.Action).IsRequired().HasMaxLength(20);
            entry.Property(a => a.Field).HasMaxLength(50);
            entry.Property(a => a.Reason).HasMaxLength(EquipmentRules.MaxReason);
            entry.HasIndex(a => new { a.EquipmentId, a.AtUtc });
            entry.HasIndex(a => a.EntityId);
        });
    }
}
