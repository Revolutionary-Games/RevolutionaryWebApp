using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    public partial class AddLatestFieldToThriveVersion : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "latest",
                table: "launcher_thrive_versions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_launcher_thrive_versions_stable_latest",
                table: "launcher_thrive_versions",
                columns: new[] { "stable", "latest" },
                unique: true,
                filter: "(latest IS TRUE)");

            migrationBuilder.CreateIndex(
                name: "ix_launcher_launcher_versions_latest",
                table: "launcher_launcher_versions",
                column: "latest",
                unique: true,
                filter: "(latest IS TRUE)");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_launcher_thrive_versions_stable_latest",
                table: "launcher_thrive_versions");

            migrationBuilder.DropIndex(
                name: "ix_launcher_launcher_versions_latest",
                table: "launcher_launcher_versions");

            migrationBuilder.DropColumn(
                name: "latest",
                table: "launcher_thrive_versions");
        }
    }
}
