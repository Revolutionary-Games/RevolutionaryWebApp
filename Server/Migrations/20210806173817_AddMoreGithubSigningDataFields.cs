using Microsoft.EntityFrameworkCore.Migrations;

namespace RevolutionaryWebApp.Server.Migrations
{
    public partial class AddMoreGithubSigningDataFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "github_email",
                table: "in_progress_cla_signatures",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "github_user_id",
                table: "in_progress_cla_signatures",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "github_email",
                table: "cla_signatures",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "github_user_id",
                table: "cla_signatures",
                type: "bigint",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "github_email",
                table: "in_progress_cla_signatures");

            migrationBuilder.DropColumn(
                name: "github_user_id",
                table: "in_progress_cla_signatures");

            migrationBuilder.DropColumn(
                name: "github_email",
                table: "cla_signatures");

            migrationBuilder.DropColumn(
                name: "github_user_id",
                table: "cla_signatures");
        }
    }
}
