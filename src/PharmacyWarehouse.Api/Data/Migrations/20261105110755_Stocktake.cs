using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmacyWarehouse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Stocktake : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockMovements_ReversesId",
                table: "StockMovements");

            migrationBuilder.CreateTable(
                name: "Stocktakes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LocationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartedById = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ClosedById = table.Column<Guid>(type: "TEXT", nullable: true),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stocktakes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Stocktakes_StorageLocations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "StorageLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StocktakeCounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StocktakeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExpectedOnHand = table.Column<int>(type: "INTEGER", nullable: false),
                    Counted = table.Column<int>(type: "INTEGER", nullable: false),
                    AtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AdjustmentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StocktakeCounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StocktakeCounts_Batches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "Batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StocktakeCounts_StockMovements_AdjustmentId",
                        column: x => x.AdjustmentId,
                        principalTable: "StockMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StocktakeCounts_Stocktakes_StocktakeId",
                        column: x => x.StocktakeId,
                        principalTable: "Stocktakes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_ReversesId",
                table: "StockMovements",
                column: "ReversesId",
                unique: true,
                filter: "\"ReversesId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_StocktakeCountId",
                table: "StockMovements",
                column: "StocktakeCountId",
                unique: true,
                filter: "\"StocktakeCountId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeCounts_AdjustmentId",
                table: "StocktakeCounts",
                column: "AdjustmentId");

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeCounts_BatchId_AtUtc",
                table: "StocktakeCounts",
                columns: new[] { "BatchId", "AtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeCounts_StocktakeId",
                table: "StocktakeCounts",
                column: "StocktakeId");

            migrationBuilder.CreateIndex(
                name: "IX_Stocktakes_ClosedAtUtc",
                table: "Stocktakes",
                column: "ClosedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Stocktakes_OpenLocation",
                table: "Stocktakes",
                column: "LocationId",
                unique: true,
                filter: "\"ClosedAtUtc\" IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_StockMovements_StocktakeCounts_StocktakeCountId",
                table: "StockMovements",
                column: "StocktakeCountId",
                principalTable: "StocktakeCounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("برگشت این migration پشتیبانی نمی‌شود (حرکت‌های انبارگردانی و triggerهای StockMovements از بین می‌رفتند)؛ از پشتیبان استفاده شود.");
    }
}
