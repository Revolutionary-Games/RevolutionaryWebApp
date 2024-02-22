using Microsoft.EntityFrameworkCore.Migrations;

namespace RevolutionaryWebApp.Server.Migrations
{
    public partial class AddUniqueNameIndexToSecrets : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_ci_secrets_ci_project_id_secret_name",
                table: "ci_secrets",
                columns: new[] { "ci_project_id", "secret_name" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ci_secrets_ci_project_id_secret_name",
                table: "ci_secrets");
        }
    }
}
