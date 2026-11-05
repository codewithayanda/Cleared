using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cleared.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceCreatedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "created_at",
                table: "invoices",
                type: "timestamp with time zone",
                nullable: true);

            // Invoices that exist already: the first audit entry is the nearest record of when each began,
            // then the issue date, and a draft with neither counts as made now.
            migrationBuilder.Sql(
                """
                UPDATE invoices
                SET created_at = COALESCE(
                    (SELECT MIN(a.occurred_at)
                     FROM audit_logs a
                     WHERE a.tenant_id = invoices.tenant_id AND a.entity_type = 'Invoice' AND a.entity_id = invoices.id),
                    invoices.issue_date::timestamp AT TIME ZONE 'UTC',
                    now());
                """);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "created_at",
                table: "invoices",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "created_at",
                table: "invoices");
        }
    }
}
