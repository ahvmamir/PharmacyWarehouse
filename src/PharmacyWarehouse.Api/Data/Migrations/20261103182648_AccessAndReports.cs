using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmacyWarehouse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AccessAndReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "HasNoAssetNumber",
                table: "Equipment",
                newName: "HasTemporaryAssetNumber");

            migrationBuilder.AddColumn<string>(
                name: "KeyboardModel",
                table: "Equipment",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MouseModel",
                table: "Equipment",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                table: "Equipment",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.Sql("UPDATE Equipment SET UpdatedAtUtc = CreatedAtUtc;");

            // هر دستگاهی شماره می‌گیرد. هنوز داده‌ی واقعی نیست؛ این SQL از رهگیر تاریخچه رد می‌شود و فقط یک بار اجرا می‌شود.
            migrationBuilder.Sql("UPDATE Equipment SET HasTemporaryAssetNumber = 1, " +
                "AssetNumber = 'TEMP-' || printf('%06d', rowid), AssetNumberKey = 'TEMP-' || printf('%06d', rowid) " +
                "WHERE AssetNumberKey IS NULL;");

            // ظرفیت فقط برای RAM و SSD و HDD معنی دارد.
            migrationBuilder.Sql("UPDATE Components SET Capacity = NULL WHERE Kind NOT IN ('Ram', 'Ssd', 'Hdd');");

            // نقش‌های پیش‌فرض. روی دیتابیسی که از قبل این نقش‌ها را دارد چیزی تکرار نمی‌شود.
            migrationBuilder.Sql("INSERT INTO AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp) " +
                "SELECT 'A0000000-0000-0000-0000-000000000001', 'Admin', 'ADMIN', 'a' " +
                "WHERE NOT EXISTS (SELECT 1 FROM AspNetRoles WHERE NormalizedName = 'ADMIN');");
            migrationBuilder.Sql("INSERT INTO AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp) " +
                "SELECT 'A0000000-0000-0000-0000-000000000002', 'Technician', 'TECHNICIAN', 't' " +
                "WHERE NOT EXISTS (SELECT 1 FROM AspNetRoles WHERE NormalizedName = 'TECHNICIAN');");

            foreach (var permission in new[]
                     {
                         "equipment.view", "equipment.manage", "equipment.move", "equipment.link",
                         "equipment.status", "reports.view", "data.export"
                     })
            {
                migrationBuilder.Sql("INSERT INTO AspNetRoleClaims (RoleId, ClaimType, ClaimValue) " +
                    $"SELECT Id, 'perm', '{permission}' FROM AspNetRoles WHERE NormalizedName = 'TECHNICIAN';");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "KeyboardModel",
                table: "Equipment");

            migrationBuilder.DropColumn(
                name: "MouseModel",
                table: "Equipment");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                table: "Equipment");

            migrationBuilder.RenameColumn(
                name: "HasTemporaryAssetNumber",
                table: "Equipment",
                newName: "HasNoAssetNumber");
        }
    }
}
