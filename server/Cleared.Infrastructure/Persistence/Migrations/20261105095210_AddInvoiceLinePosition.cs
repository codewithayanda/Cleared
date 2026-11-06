using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cleared.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceLinePosition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "position",
                table: "invoice_line_items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Lines saved before this had no position, and were read back in the order of their ids. Number
            // them in that order, so no invoice already made changes the order it shows its lines in.
            migrationBuilder.Sql(
                """
                UPDATE invoice_line_items
                SET position = numbered.position
                FROM (
                    SELECT id, (ROW_NUMBER() OVER (PARTITION BY invoice_id ORDER BY id) - 1)::integer AS position
                    FROM invoice_line_items
                ) AS numbered
                WHERE invoice_line_items.id = numbered.id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "position",
                table: "invoice_line_items");
        }
    }
}
