using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace RevolutionaryWebApp.Server.Migrations
{
    public partial class AddJobRunOnServerInfo : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ran_on_server",
                table: "ci_jobs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "time_waiting_for_server",
                table: "ci_jobs",
                type: "interval",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ran_on_server",
                table: "ci_jobs");

            migrationBuilder.DropColumn(
                name: "time_waiting_for_server",
                table: "ci_jobs");
        }
    }
}
