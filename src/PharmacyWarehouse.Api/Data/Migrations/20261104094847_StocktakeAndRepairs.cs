using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmacyWarehouse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class StocktakeAndRepairs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastVerifiedAtUtc",
                table: "Equipment",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Repairs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EquipmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Vendor = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Problem = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ReturnedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Result = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CostRials = table.Column<long>(type: "INTEGER", nullable: true),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Repairs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Repairs_Equipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Repairs_EquipmentId_SentAtUtc",
                table: "Repairs",
                columns: new[] { "EquipmentId", "SentAtUtc" });

            // انبارگردانی کار روزانه‌ی تکنسین است؛ مجوز تازه به نقش پیش‌فرض تکنسین هم داده می‌شود (بدون تکرار).
            migrationBuilder.Sql("INSERT INTO AspNetRoleClaims (RoleId, ClaimType, ClaimValue) " +
                "SELECT Id, 'perm', 'equipment.verify' FROM AspNetRoles WHERE NormalizedName = 'TECHNICIAN' " +
                "AND NOT EXISTS (SELECT 1 FROM AspNetRoleClaims c WHERE c.RoleId = AspNetRoles.Id AND c.ClaimType = 'perm' AND c.ClaimValue = 'equipment.verify');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM AspNetRoleClaims WHERE ClaimType = 'perm' AND ClaimValue = 'equipment.verify';");

            migrationBuilder.DropTable(
                name: "Repairs");

            migrationBuilder.DropColumn(
                name: "LastVerifiedAtUtc",
                table: "Equipment");
        }
    }
}
