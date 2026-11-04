using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmacyWarehouse.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AuditAppendOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE TRIGGER AuditEntries_NoUpdate BEFORE UPDATE ON AuditEntries BEGIN SELECT RAISE(ABORT, 'audit is append-only'); END;");
            migrationBuilder.Sql("CREATE TRIGGER AuditEntries_NoDelete BEFORE DELETE ON AuditEntries BEGIN SELECT RAISE(ABORT, 'audit is append-only'); END;");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER AuditEntries_NoUpdate;");
            migrationBuilder.Sql("DROP TRIGGER AuditEntries_NoDelete;");

        }
    }
}
