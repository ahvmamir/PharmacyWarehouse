using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmacyWarehouse.Api.Data.Migrations
{
    /// <inheritdoc />
    // Stocktake یک FK به StockMovements اضافه کرد و SQLite جدول را در انتهای همان migration بازسازی می‌کند؛
    // triggerهای فقط‌اضافه‌شونده با جدول از بین می‌روند، پس این migration جدا و بعد از آن، triggerها را دوباره می‌سازد.
    public partial class StocktakeTriggers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS StockMovements_NoUpdate;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS StockMovements_NoDelete;");
            migrationBuilder.Sql("CREATE TRIGGER StockMovements_NoUpdate BEFORE UPDATE ON StockMovements BEGIN SELECT RAISE(ABORT, 'stock movements are append-only'); END;");
            migrationBuilder.Sql("CREATE TRIGGER StockMovements_NoDelete BEFORE DELETE ON StockMovements BEGIN SELECT RAISE(ABORT, 'stock movements are append-only'); END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("برگشت این migration پشتیبانی نمی‌شود (triggerهای StockMovements از بین می‌رفتند)؛ از پشتیبان استفاده شود.");
    }
}
