using Microsoft.EntityFrameworkCore.Migrations;

namespace RevolutionaryWebApp.Server.Migrations
{
    public partial class AddReporterEmailToCrashReport : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "logs",
                table: "crash_reports",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reporter_email",
                table: "crash_reports",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_crash_reports_description",
                table: "crash_reports",
                column: "description");

            migrationBuilder.CreateIndex(
                name: "ix_crash_reports_primary_callstack",
                table: "crash_reports",
                column: "primary_callstack");

            migrationBuilder.CreateIndex(
                name: "ix_crash_reports_reporter_email",
                table: "crash_reports",
                column: "reporter_email");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_crash_reports_description",
                table: "crash_reports");

            migrationBuilder.DropIndex(
                name: "ix_crash_reports_primary_callstack",
                table: "crash_reports");

            migrationBuilder.DropIndex(
                name: "ix_crash_reports_reporter_email",
                table: "crash_reports");

            migrationBuilder.DropColumn(
                name: "reporter_email",
                table: "crash_reports");

            migrationBuilder.AlterColumn<string>(
                name: "logs",
                table: "crash_reports",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");
        }
    }
}
