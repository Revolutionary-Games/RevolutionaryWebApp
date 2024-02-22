using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace RevolutionaryWebApp.Server.Migrations
{
    public partial class AddMoreCiBuildProperties : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "created_at",
                table: "ci_builds",
                type: "timestamp without time zone",
                nullable: false,
                defaultValueSql: "timezone('utc', now())");

            migrationBuilder.AddColumn<int>(
                name: "status",
                table: "ci_builds",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "created_at",
                table: "ci_builds");

            migrationBuilder.DropColumn(
                name: "status",
                table: "ci_builds");
        }
    }
}
