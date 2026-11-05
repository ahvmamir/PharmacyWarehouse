using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmacyWarehouse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Withdrawals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Withdrawals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsLate = table.Column<bool>(type: "INTEGER", nullable: false),
                    AfterStocktake = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    ReviewedById = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReviewNote = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SelfReviewed = table.Column<bool>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Withdrawals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Withdrawals_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_WithdrawalId",
                table: "StockMovements",
                column: "WithdrawalId");

            migrationBuilder.CreateIndex(
                name: "IX_Withdrawals_ProductId",
                table: "Withdrawals",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Withdrawals_Status_AtUtc",
                table: "Withdrawals",
                columns: new[] { "Status", "AtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Withdrawals_UserId_OccurredAtUtc",
                table: "Withdrawals",
                columns: new[] { "UserId", "OccurredAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_StockMovements_Withdrawals_WithdrawalId",
                table: "StockMovements",
                column: "WithdrawalId",
                principalTable: "Withdrawals",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StockMovements_Withdrawals_WithdrawalId",
                table: "StockMovements");

            migrationBuilder.DropTable(
                name: "Withdrawals");

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_WithdrawalId",
                table: "StockMovements");

        }
    }
}
