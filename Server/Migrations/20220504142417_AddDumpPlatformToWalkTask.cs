using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    public partial class AddDumpPlatformToWalkTask : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "special_walk_type",
                table: "stackwalk_tasks");

            migrationBuilder.AddColumn<int>(
                name: "stackwalk_platform",
                table: "stackwalk_tasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "stackwalk_platform",
                table: "stackwalk_tasks");

            migrationBuilder.AddColumn<string>(
                name: "special_walk_type",
                table: "stackwalk_tasks",
                type: "text",
                nullable: true);
        }
    }
}
