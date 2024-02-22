using Microsoft.EntityFrameworkCore.Migrations;

namespace RevolutionaryWebApp.Server.Migrations
{
    public partial class TweakServerDiskColumns : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "available_disk_space",
                table: "controlled_servers");

            migrationBuilder.AddColumn<bool>(
                name: "clean_up_queued",
                table: "controlled_servers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "used_disk_space",
                table: "controlled_servers",
                type: "integer",
                nullable: false,
                defaultValue: -1);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "clean_up_queued",
                table: "controlled_servers");

            migrationBuilder.DropColumn(
                name: "used_disk_space",
                table: "controlled_servers");

            migrationBuilder.AddColumn<long>(
                name: "available_disk_space",
                table: "controlled_servers",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }
    }
}
