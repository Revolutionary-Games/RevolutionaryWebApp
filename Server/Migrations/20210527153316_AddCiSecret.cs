using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace RevolutionaryWebApp.Server.Migrations
{
    public partial class AddCiSecret : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ci_secrets",
                columns: table => new
                {
                    ci_project_id = table.Column<long>(type: "bigint", nullable: false),
                    ci_secret_id = table.Column<long>(type: "bigint", nullable: false),
                    used_for_build_types = table.Column<int>(type: "integer", nullable: false),
                    secret_name = table.Column<string>(type: "text", nullable: false),
                    secret_content = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ci_secrets", x => new { x.ci_project_id, x.ci_secret_id });
                    table.ForeignKey(
                        name: "fk_ci_secrets_ci_projects_ci_project_id",
                        column: x => x.ci_project_id,
                        principalTable: "ci_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ci_secrets");
        }
    }
}
