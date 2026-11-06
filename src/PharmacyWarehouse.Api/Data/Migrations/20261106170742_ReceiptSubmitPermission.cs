using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmacyWarehouse.Api.Data.Migrations
{
    // فقط داده: مجوز تازه‌ی receipt.submit به نقش‌های پیش‌فرض Staff و Keeper داده می‌شود (اگر نقش هست و claim ندارد).
    /// <inheritdoc />
    public partial class ReceiptSubmitPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var role in new[] { "STAFF", "KEEPER" })
            {
                migrationBuilder.Sql("INSERT INTO AspNetRoleClaims (RoleId, ClaimType, ClaimValue) " +
                    $"SELECT r.Id, 'perm', 'receipt.submit' FROM AspNetRoles r WHERE r.NormalizedName = '{role}' " +
                    "AND NOT EXISTS (SELECT 1 FROM AspNetRoleClaims c WHERE c.RoleId = r.Id AND c.ClaimType = 'perm' AND c.ClaimValue = 'receipt.submit');");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Down is not supported.");
        }
    }
}
