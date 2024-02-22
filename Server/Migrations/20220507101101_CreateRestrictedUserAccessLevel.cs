using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    public partial class CreateRestrictedUserAccessLevel : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "restricted",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("UPDATE storage_items SET read_access = read_access + 1 WHERE read_access > 0;");
            migrationBuilder.Sql("UPDATE storage_items SET write_access = write_access + 1 WHERE write_access > 0;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "restricted",
                table: "users");

            migrationBuilder.Sql("UPDATE storage_items SET read_access = read_access - 1 WHERE read_access > 1;");
            migrationBuilder.Sql("UPDATE storage_items SET write_access = write_access - 1 WHERE write_access > 1;");
        }
    }
}
