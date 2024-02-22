using Microsoft.EntityFrameworkCore.Migrations;

namespace RevolutionaryWebApp.Server.Migrations
{
    public partial class AddHashedIdToSession : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "hashed_id",
                table: "sessions",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_sessions_hashed_id",
                table: "sessions",
                column: "hashed_id",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sessions_hashed_id",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "hashed_id",
                table: "sessions");
        }
    }
}
