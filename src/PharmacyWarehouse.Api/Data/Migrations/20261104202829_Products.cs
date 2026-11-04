using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmacyWarehouse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Products : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NameKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Brand = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    BrandKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    PackDescription = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    PackKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    LocationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MinStock = table.Column<int>(type: "INTEGER", nullable: true),
                    UnitsPerPack = table.Column<int>(type: "INTEGER", nullable: false),
                    RequiresExpiry = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowExpiredWithdrawal = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    SearchText = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Products_StorageLocations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "StorageLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Batches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BatchNumber = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    BatchKey = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ExpiryDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ExpiryPrecision = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    ExpiryCalendar = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    LocationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UnitCost = table.Column<long>(type: "INTEGER", nullable: true),
                    OnHand = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Batches", x => x.Id);
                    table.CheckConstraint("CK_Batches_OnHand", "OnHand >= 0");
                    table.ForeignKey(
                        name: "FK_Batches_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Batches_StorageLocations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "StorageLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductBarcodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CodeKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductBarcodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductBarcodes_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    AtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    WithdrawalId = table.Column<Guid>(type: "TEXT", nullable: true),
                    StocktakeCountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReversesId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockMovements_Batches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "Batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockMovements_StockMovements_ReversesId",
                        column: x => x.ReversesId,
                        principalTable: "StockMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Batches_LocationId",
                table: "Batches",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Batches_ProductId_BatchKey",
                table: "Batches",
                columns: new[] { "ProductId", "BatchKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductBarcodes_CodeKey",
                table: "ProductBarcodes",
                column: "CodeKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductBarcodes_ProductId",
                table: "ProductBarcodes",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_LocationId",
                table: "Products",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_NameKey_BrandKey_PackKey",
                table: "Products",
                columns: new[] { "NameKey", "BrandKey", "PackKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_BatchId_AtUtc",
                table: "StockMovements",
                columns: new[] { "BatchId", "AtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_ReversesId",
                table: "StockMovements",
                column: "ReversesId");

            // دفتر حرکت فقط اضافه‌شونده است: هر UPDATE یا DELETE مستقیم رد می‌شود.
            migrationBuilder.Sql("CREATE TRIGGER StockMovements_NoUpdate BEFORE UPDATE ON StockMovements BEGIN SELECT RAISE(ABORT, 'stock movements are append-only'); END;");
            migrationBuilder.Sql("CREATE TRIGGER StockMovements_NoDelete BEFORE DELETE ON StockMovements BEGIN SELECT RAISE(ABORT, 'stock movements are append-only'); END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER StockMovements_NoUpdate;");
            migrationBuilder.Sql("DROP TRIGGER StockMovements_NoDelete;");

            migrationBuilder.DropTable(
                name: "ProductBarcodes");

            migrationBuilder.DropTable(
                name: "StockMovements");

            migrationBuilder.DropTable(
                name: "Batches");

            migrationBuilder.DropTable(
                name: "Products");
        }
    }
}
