using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cleared.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSignInThrottles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sign_in_throttles",
                columns: table => new
                {
                    email_hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sign_in_throttles", x => x.email_hash);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sign_in_throttles_updated_at",
                table: "sign_in_throttles",
                column: "updated_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sign_in_throttles");
        }
    }
}
