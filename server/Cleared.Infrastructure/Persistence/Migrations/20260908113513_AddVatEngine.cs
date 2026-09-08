using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cleared.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVatEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "document_type",
                table: "invoices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "supply_date",
                table: "invoices",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "vat_rate_applied",
                table: "invoices",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "line_vat_amount",
                table: "invoice_line_items",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "line_vat_currency",
                table: "invoice_line_items",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "vat_treatment",
                table: "invoice_line_items",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "address",
                table: "customers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "credit_note_number_sequences",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    next_value = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_note_number_sequences", x => new { x.tenant_id, x.year });
                });

            migrationBuilder.CreateTable(
                name: "credit_notes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    issue_date = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_notes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "vat_rates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rate = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vat_rates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "credit_note_line_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    credit_note_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_line_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    vat_treatment = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    line_subtotal_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    line_subtotal_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    line_vat_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    line_vat_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    unit_price_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    unit_price_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_note_line_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_credit_note_line_items_credit_notes_credit_note_id",
                        column: x => x.credit_note_id,
                        principalTable: "credit_notes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_credit_note_line_items_credit_note_id",
                table: "credit_note_line_items",
                column: "credit_note_id");

            migrationBuilder.CreateIndex(
                name: "ix_credit_note_line_items_invoice_line_item_id",
                table: "credit_note_line_items",
                column: "invoice_line_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_credit_notes_tenant_id_invoice_id",
                table: "credit_notes",
                columns: new[] { "tenant_id", "invoice_id" });

            migrationBuilder.CreateIndex(
                name: "ix_credit_notes_tenant_id_number",
                table: "credit_notes",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vat_rates_effective_from",
                table: "vat_rates",
                column: "effective_from");

            // South Africa's standard VAT rate has been 15% since 1 April 2018. A fixed,
            // well-known id (not Guid.NewGuid()) so this seed is deterministic across
            // every environment the migration runs in, and so Down() can remove exactly
            // this row.
            migrationBuilder.InsertData(
                table: "vat_rates",
                columns: new[] { "id", "rate", "effective_from", "effective_to" },
                values: new object[] { new Guid("00000000-0000-0000-0000-000000000015"), 0.15m, new DateOnly(2018, 4, 1), null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "vat_rates",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000015"));

            migrationBuilder.DropTable(
                name: "credit_note_line_items");

            migrationBuilder.DropTable(
                name: "credit_note_number_sequences");

            migrationBuilder.DropTable(
                name: "vat_rates");

            migrationBuilder.DropTable(
                name: "credit_notes");

            migrationBuilder.DropColumn(
                name: "document_type",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "supply_date",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "vat_rate_applied",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "line_vat_amount",
                table: "invoice_line_items");

            migrationBuilder.DropColumn(
                name: "line_vat_currency",
                table: "invoice_line_items");

            migrationBuilder.DropColumn(
                name: "vat_treatment",
                table: "invoice_line_items");

            migrationBuilder.DropColumn(
                name: "address",
                table: "customers");
        }
    }
}
