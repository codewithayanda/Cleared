using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cleared.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentRecordedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfills existing rows with "now" rather than DateTimeOffset.MinValue — there
            // are no production payments yet, so the exact backfilled value doesn't matter,
            // but a sane timestamp is safer than a year-1 placeholder leaking into a UI.
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "recorded_at",
                table: "payments",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "recorded_at",
                table: "payments");
        }
    }
}
