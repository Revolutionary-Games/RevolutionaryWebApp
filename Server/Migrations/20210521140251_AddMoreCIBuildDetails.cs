using Microsoft.EntityFrameworkCore.Migrations;

namespace RevolutionaryWebApp.Server.Migrations
{
    public partial class AddMoreCIBuildDetails : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "branch",
                table: "ci_builds",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "commit_message",
                table: "ci_builds",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "commits",
                table: "ci_builds",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_safe",
                table: "ci_builds",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "previous_commit",
                table: "ci_builds",
                type: "text",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "branch",
                table: "ci_builds");

            migrationBuilder.DropColumn(
                name: "commit_message",
                table: "ci_builds");

            migrationBuilder.DropColumn(
                name: "commits",
                table: "ci_builds");

            migrationBuilder.DropColumn(
                name: "is_safe",
                table: "ci_builds");

            migrationBuilder.DropColumn(
                name: "previous_commit",
                table: "ci_builds");
        }
    }
}
