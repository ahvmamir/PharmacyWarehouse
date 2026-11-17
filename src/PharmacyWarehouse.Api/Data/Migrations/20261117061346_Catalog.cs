using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmacyWarehouse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Catalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CatalogImports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Inserted = table.Column<int>(type: "INTEGER", nullable: false),
                    Empty = table.Column<int>(type: "INTEGER", nullable: false),
                    Invalid = table.Column<int>(type: "INTEGER", nullable: false),
                    DuplicateGtin = table.Column<int>(type: "INTEGER", nullable: false),
                    AtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogImports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CatalogItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Irc = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    Gtin = table.Column<string>(type: "TEXT", maxLength: 14, nullable: false),
                    GenericCode = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    GenericName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    BrandName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    PersianBrandName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    LicenseOwner = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    Producer = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    ProducerCountry = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    PackageCount = table.Column<int>(type: "INTEGER", nullable: true),
                    ConsumerPrice = table.Column<long>(type: "INTEGER", nullable: true),
                    IsTemporaryIrc = table.Column<bool>(type: "INTEGER", nullable: false),
                    SearchKey = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogImports_AtUtc",
                table: "CatalogImports",
                column: "AtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogItems_Gtin",
                table: "CatalogItems",
                column: "Gtin");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogItems_SearchKey",
                table: "CatalogItems",
                column: "SearchKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CatalogImports");

            migrationBuilder.DropTable(
                name: "CatalogItems");
        }
    }
}
