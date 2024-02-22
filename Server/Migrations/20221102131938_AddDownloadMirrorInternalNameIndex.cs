using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    public partial class AddDownloadMirrorInternalNameIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_launcher_download_mirrors_internal_name",
                table: "launcher_download_mirrors",
                column: "internal_name",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_launcher_download_mirrors_internal_name",
                table: "launcher_download_mirrors");
        }
    }
}
