using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmacyWarehouse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class StocktakePeriods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CategoryId",
                table: "Stocktakes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PeriodId",
                table: "Stocktakes",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("b0000000-0000-0000-0000-000000000001"));

            migrationBuilder.AddColumn<string>(
                name: "DismissNote",
                table: "StocktakeCounts",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DismissedAtUtc",
                table: "StocktakeCounts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DismissedById",
                table: "StocktakeCounts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StocktakePeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    StartedById = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ClosedById = table.Column<Guid>(type: "TEXT", nullable: true),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StocktakePeriods", x => x.Id);
                });

            // نوبت‌های پیش از دوره‌بندی (بخش ۱۵.۳ spec): ستون PeriodId پیش‌فرض LegacyId دارد، پس همه زیر این دوره‌ی ساختگی می‌روند.
            // فقط اگر نوبتی هست ساخته می‌شود؛ اگر نوبت بازی هست دوره باز می‌ماند، وگرنه بسته است تا دوره‌ی تازه را نپوشاند.
            // این SQL پیش از بازسازی جدول Stocktakes (AddForeignKey در انتها) اجرا می‌شود، پس ردیف دوره پیش از کلید خارجی هست.
            migrationBuilder.Sql(
                "INSERT INTO StocktakePeriods (Id, Name, StartedById, StartedAtUtc, ClosedAtUtc, ClosedById, Version) " +
                "SELECT 'B0000000-0000-0000-0000-000000000001', 'پیش از دوره‌بندی', " +
                "(SELECT StartedById FROM Stocktakes ORDER BY StartedAtUtc LIMIT 1), " +
                "(SELECT MIN(StartedAtUtc) FROM Stocktakes), " +
                "CASE WHEN EXISTS (SELECT 1 FROM Stocktakes WHERE ClosedAtUtc IS NULL) THEN NULL ELSE strftime('%Y-%m-%d %H:%M:%S', 'now') END, " +
                "NULL, 1 " +
                "WHERE EXISTS (SELECT 1 FROM Stocktakes);");

            migrationBuilder.CreateTable(
                name: "StocktakePeriodCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PeriodId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CategoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StocktakePeriodCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StocktakePeriodCategories_ProductCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "ProductCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StocktakePeriodCategories_StocktakePeriods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "StocktakePeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StocktakePeriodLocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PeriodId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LocationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StocktakePeriodLocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StocktakePeriodLocations_StocktakePeriods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "StocktakePeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StocktakePeriodLocations_StorageLocations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "StorageLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Stocktakes_CategoryId",
                table: "Stocktakes",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Stocktakes_PeriodId",
                table: "Stocktakes",
                column: "PeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_StocktakePeriodCategories_CategoryId",
                table: "StocktakePeriodCategories",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_StocktakePeriodCategories_PeriodId_CategoryId",
                table: "StocktakePeriodCategories",
                columns: new[] { "PeriodId", "CategoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StocktakePeriodLocations_LocationId",
                table: "StocktakePeriodLocations",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_StocktakePeriodLocations_PeriodId_LocationId",
                table: "StocktakePeriodLocations",
                columns: new[] { "PeriodId", "LocationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StocktakePeriods_ClosedAtUtc",
                table: "StocktakePeriods",
                column: "ClosedAtUtc");

            migrationBuilder.AddForeignKey(
                name: "FK_Stocktakes_ProductCategories_CategoryId",
                table: "Stocktakes",
                column: "CategoryId",
                principalTable: "ProductCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Stocktakes_StocktakePeriods_PeriodId",
                table: "Stocktakes",
                column: "PeriodId",
                principalTable: "StocktakePeriods",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // برگشت دوره‌ها را دور می‌ریخت و نوبت‌ها را بی‌دوره می‌کرد؛ پشتیبانی نمی‌شود، از پشتیبان استفاده کنید.
            throw new NotSupportedException("Downgrade below StocktakePeriods is not supported; restore a backup instead.");
        }
    }
}
