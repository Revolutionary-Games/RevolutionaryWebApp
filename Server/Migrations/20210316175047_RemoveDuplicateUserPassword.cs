using Microsoft.EntityFrameworkCore.Migrations;

namespace RevolutionaryWebApp.Server.Migrations
{
    public partial class RemoveDuplicateUserPassword : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "password_digest",
                table: "users");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "password_digest",
                table: "users",
                type: "text",
                nullable: true);
        }
    }
}
