using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cleared.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIssuedPartyDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "customer_address",
                table: "invoices",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "customer_name",
                table: "invoices",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "customer_vat_number",
                table: "invoices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "supplier_address",
                table: "invoices",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "supplier_company_name",
                table: "invoices",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "supplier_trading_name",
                table: "invoices",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "supplier_vat_number",
                table: "invoices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // Invoices issued before these columns existed get the details their customer and company have
            // now, which is the nearest record there is. Drafts keep none until they are issued.
            migrationBuilder.Sql(
                """
                UPDATE invoices
                SET customer_name = customers.name,
                    customer_address = customers.address,
                    customer_vat_number = customers.vat_number
                FROM customers
                WHERE customers.id = invoices.customer_id
                  AND customers.tenant_id = invoices.tenant_id
                  AND invoices.status <> 'Draft';
                """);

            migrationBuilder.Sql(
                """
                UPDATE invoices
                SET supplier_company_name = tenants.company_name,
                    supplier_trading_name = tenants.trading_name,
                    supplier_address = tenants.address,
                    supplier_vat_number = tenants.vat_number
                FROM tenants
                WHERE tenants.id = invoices.tenant_id
                  AND invoices.status <> 'Draft';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "customer_address",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "customer_name",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "customer_vat_number",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "supplier_address",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "supplier_company_name",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "supplier_trading_name",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "supplier_vat_number",
                table: "invoices");
        }
    }
}
