using Microsoft.EntityFrameworkCore.Migrations;

namespace RevolutionaryWebApp.Server.Migrations
{
    public partial class AddCondensedCrashDumpToReport : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_crash_reports_primary_callstack",
                table: "crash_reports");

            migrationBuilder.AddColumn<string>(
                name: "condensed_callstack",
                table: "crash_reports",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_crash_reports_condensed_callstack",
                table: "crash_reports",
                column: "condensed_callstack");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_crash_reports_condensed_callstack",
                table: "crash_reports");

            migrationBuilder.DropColumn(
                name: "condensed_callstack",
                table: "crash_reports");

            migrationBuilder.CreateIndex(
                name: "ix_crash_reports_primary_callstack",
                table: "crash_reports",
                column: "primary_callstack");
        }
    }
}
