using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ThriveDevCenter.Server.Migrations
{
    public partial class AddPerSectionTiming : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "finished_at",
                table: "ci_job_output_sections",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "started_at",
                table: "ci_job_output_sections",
                type: "timestamp without time zone",
                nullable: false,
                defaultValueSql: "timezone('utc', now())");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "finished_at",
                table: "ci_job_output_sections");

            migrationBuilder.DropColumn(
                name: "started_at",
                table: "ci_job_output_sections");
        }
    }
}
