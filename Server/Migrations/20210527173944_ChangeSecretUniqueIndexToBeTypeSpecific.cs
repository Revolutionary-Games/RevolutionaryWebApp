using Microsoft.EntityFrameworkCore.Migrations;

namespace ThriveDevCenter.Server.Migrations
{
    public partial class ChangeSecretUniqueIndexToBeTypeSpecific : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ci_secrets_ci_project_id_secret_name",
                table: "ci_secrets");

            migrationBuilder.CreateIndex(
                name: "ix_ci_secrets_ci_project_id_secret_name_used_for_build_types",
                table: "ci_secrets",
                columns: new[] { "ci_project_id", "secret_name", "used_for_build_types" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ci_secrets_ci_project_id_secret_name_used_for_build_types",
                table: "ci_secrets");

            migrationBuilder.CreateIndex(
                name: "ix_ci_secrets_ci_project_id_secret_name",
                table: "ci_secrets",
                columns: new[] { "ci_project_id", "secret_name" },
                unique: true);
        }
    }
}
