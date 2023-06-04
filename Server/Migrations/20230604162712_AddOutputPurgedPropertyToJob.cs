using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThriveDevCenter.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddOutputPurgedPropertyToJob : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "output_purged",
                table: "ci_jobs",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "output_purged",
                table: "ci_jobs");
        }
    }
}
