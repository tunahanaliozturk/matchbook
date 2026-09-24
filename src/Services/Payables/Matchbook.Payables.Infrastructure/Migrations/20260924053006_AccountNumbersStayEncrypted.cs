using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Matchbook.Payables.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AccountNumbersStayEncrypted : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The columns now hold the ciphertext Suppliers sends (ADR 0007). A database that already held plain
            // IBANs fails the check constraints below on purpose: those rows cannot be encrypted in SQL, and mixing
            // the two formats would be worse than stopping.
            migrationBuilder.RenameColumn(
                name: "account_iban",
                table: "suppliers",
                newName: "account_protected_iban");

            migrationBuilder.RenameColumn(
                name: "iban",
                table: "payment_run_creditors",
                newName: "protected_iban");

            migrationBuilder.AddColumn<string>(
                name: "account_iban_last_four",
                table: "suppliers",
                type: "character varying(4)",
                maxLength: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "iban_last_four",
                table: "payment_run_creditors",
                type: "character varying(4)",
                maxLength: 4,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddCheckConstraint(
                name: "ck_suppliers_account_iban_protected",
                table: "suppliers",
                sql: "account_protected_iban IS NULL OR account_protected_iban LIKE 'v1.%'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_run_creditors_iban_protected",
                table: "payment_run_creditors",
                sql: "protected_iban LIKE 'v1.%'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_suppliers_account_iban_protected",
                table: "suppliers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_run_creditors_iban_protected",
                table: "payment_run_creditors");

            migrationBuilder.DropColumn(
                name: "account_iban_last_four",
                table: "suppliers");

            migrationBuilder.DropColumn(
                name: "iban_last_four",
                table: "payment_run_creditors");

            migrationBuilder.RenameColumn(
                name: "account_protected_iban",
                table: "suppliers",
                newName: "account_iban");

            migrationBuilder.RenameColumn(
                name: "protected_iban",
                table: "payment_run_creditors",
                newName: "iban");
        }
    }
}
