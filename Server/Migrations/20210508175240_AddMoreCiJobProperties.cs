using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ThriveDevCenter.Server.Migrations
{
    public partial class AddMoreCiJobProperties : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "finished_at",
                table: "ci_jobs",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "succeeded",
                table: "ci_jobs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "finished_at",
                table: "ci_builds",
                type: "timestamp without time zone",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "finished_at",
                table: "ci_jobs");

            migrationBuilder.DropColumn(
                name: "succeeded",
                table: "ci_jobs");

            migrationBuilder.DropColumn(
                name: "finished_at",
                table: "ci_builds");
        }
    }
}
