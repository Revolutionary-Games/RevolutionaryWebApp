using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace RevolutionaryWebApp.Server.Migrations
{
    public partial class AddJobOutputSection : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "build_output_connect_key",
                table: "ci_jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "hashed_build_output_connect_key",
                table: "ci_jobs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "running_on_server_id",
                table: "ci_jobs",
                type: "bigint",
                nullable: false,
                defaultValue: -1L);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "ci_builds",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "ci_job_output_sections",
                columns: table => new
                {
                    ci_project_id = table.Column<long>(type: "bigint", nullable: false),
                    ci_build_id = table.Column<long>(type: "bigint", nullable: false),
                    ci_job_id = table.Column<long>(type: "bigint", nullable: false),
                    ci_job_output_section_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    output = table.Column<string>(type: "text", nullable: false),
                    output_length = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ci_job_output_sections", x => new { x.ci_project_id, x.ci_build_id, x.ci_job_id, x.ci_job_output_section_id });
                    table.ForeignKey(
                        name: "fk_ci_job_output_sections_ci_jobs_ci_project_id_ci_build_id_ci",
                        columns: x => new { x.ci_project_id, x.ci_build_id, x.ci_job_id },
                        principalTable: "ci_jobs",
                        principalColumns: new[] { "ci_project_id", "ci_build_id", "ci_job_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ci_jobs_hashed_build_output_connect_key",
                table: "ci_jobs",
                column: "hashed_build_output_connect_key",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ci_job_output_sections");

            migrationBuilder.DropIndex(
                name: "ix_ci_jobs_hashed_build_output_connect_key",
                table: "ci_jobs");

            migrationBuilder.DropColumn(
                name: "build_output_connect_key",
                table: "ci_jobs");

            migrationBuilder.DropColumn(
                name: "hashed_build_output_connect_key",
                table: "ci_jobs");

            migrationBuilder.DropColumn(
                name: "running_on_server_id",
                table: "ci_jobs");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "ci_builds");
        }
    }
}
