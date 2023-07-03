using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThriveDevCenter.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddLastLoginToSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "last_logged_in",
                table: "sessions",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_logged_in",
                table: "sessions");
        }
    }
}
