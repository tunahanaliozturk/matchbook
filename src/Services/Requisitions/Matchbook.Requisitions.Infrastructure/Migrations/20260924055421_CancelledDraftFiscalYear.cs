using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Matchbook.Requisitions.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CancelledDraftFiscalYear : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_requisitions_fiscal_year",
                table: "requisitions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_requisitions_fiscal_year",
                table: "requisitions",
                sql: "fiscal_year IS NOT NULL OR status IN ('Draft', 'Cancelled')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_requisitions_fiscal_year",
                table: "requisitions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_requisitions_fiscal_year",
                table: "requisitions",
                sql: "status = 'Draft' OR fiscal_year IS NOT NULL");
        }
    }
}
