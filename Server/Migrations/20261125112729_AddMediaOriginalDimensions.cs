using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaOriginalDimensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "original_height",
                table: "media_files",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "original_width",
                table: "media_files",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "original_height",
                table: "media_files");

            migrationBuilder.DropColumn(
                name: "original_width",
                table: "media_files");
        }
    }
}
