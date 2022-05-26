using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThriveDevCenter.Server.Migrations
{
    public partial class AddReleaseStatsRepo : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "repos_for_release_stats",
                columns: table => new
                {
                    qualified_name = table.Column<string>(type: "text", nullable: false),
                    ignore_downloads = table.Column<string>(type: "text", nullable: true),
                    show_in_all = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_repos_for_release_stats", x => x.qualified_name);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "repos_for_release_stats");
        }
    }
}
