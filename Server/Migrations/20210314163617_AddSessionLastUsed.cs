using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ThriveDevCenter.Server.Migrations
{
    public partial class AddSessionLastUsed : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "last_used",
                table: "sessions",
                type: "timestamp without time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_used",
                table: "sessions");
        }
    }
}
