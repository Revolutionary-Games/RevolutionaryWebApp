using Microsoft.EntityFrameworkCore.Migrations;

namespace ThriveDevCenter.Server.Migrations
{
    public partial class AddUserSessionsAndCascade : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_sessions_users_user_id",
                table: "sessions");

            migrationBuilder.AddForeignKey(
                name: "fk_sessions_users_user_id",
                table: "sessions",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_sessions_users_user_id",
                table: "sessions");

            migrationBuilder.AddForeignKey(
                name: "fk_sessions_users_user_id",
                table: "sessions",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
