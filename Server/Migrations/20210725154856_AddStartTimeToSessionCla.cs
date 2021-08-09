using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ThriveDevCenter.Server.Migrations
{
    public partial class AddStartTimeToSessionCla : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "started_at",
                table: "sessions",
                type: "timestamp without time zone",
                nullable: false,
                defaultValueSql: "timezone('utc', now())");

            migrationBuilder.AddColumn<DateTime>(
                name: "started_at",
                table: "in_progress_cla_signatures",
                type: "timestamp without time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "started_at",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "started_at",
                table: "in_progress_cla_signatures");
        }
    }
}
