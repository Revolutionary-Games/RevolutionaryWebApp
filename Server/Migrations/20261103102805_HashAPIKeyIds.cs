using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    /// <inheritdoc />
    public partial class HashAPIKeyIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "hashed_id",
                table: "api_keys",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_hashed_id",
                table: "api_keys",
                column: "hashed_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_api_keys_hashed_id",
                table: "api_keys");

            migrationBuilder.DropColumn(
                name: "hashed_id",
                table: "api_keys");
        }
    }
}
