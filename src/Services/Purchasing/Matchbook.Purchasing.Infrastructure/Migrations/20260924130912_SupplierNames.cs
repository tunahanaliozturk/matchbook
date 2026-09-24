using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Matchbook.Purchasing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SupplierNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "legal_name",
                table: "suppliers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "legal_name",
                table: "suppliers");
        }
    }
}
