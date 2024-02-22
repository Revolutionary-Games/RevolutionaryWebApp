using Microsoft.EntityFrameworkCore.Migrations;

namespace RevolutionaryWebApp.Server.Migrations
{
    public partial class AddPerTableHiLo : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropSequence(
                name: "EntityFrameworkHiLoSequence");

            migrationBuilder.CreateSequence(
                name: "dehydrated_objects_hilo",
                incrementBy: 10);

            migrationBuilder.CreateSequence(
                name: "lfs_objects_hilo",
                incrementBy: 10);

            migrationBuilder.CreateSequence(
                name: "project_git_files_hilo",
                incrementBy: 10);

            migrationBuilder.CreateSequence(
                name: "storage_files_hilo",
                incrementBy: 10);

            migrationBuilder.CreateSequence(
                name: "storage_item_versions_hilo",
                incrementBy: 10);

            migrationBuilder.CreateSequence(
                name: "storage_items_hilo",
                incrementBy: 10);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropSequence(
                name: "dehydrated_objects_hilo");

            migrationBuilder.DropSequence(
                name: "lfs_objects_hilo");

            migrationBuilder.DropSequence(
                name: "project_git_files_hilo");

            migrationBuilder.DropSequence(
                name: "storage_files_hilo");

            migrationBuilder.DropSequence(
                name: "storage_item_versions_hilo");

            migrationBuilder.DropSequence(
                name: "storage_items_hilo");

            migrationBuilder.CreateSequence(
                name: "EntityFrameworkHiLoSequence",
                incrementBy: 10);
        }
    }
}
