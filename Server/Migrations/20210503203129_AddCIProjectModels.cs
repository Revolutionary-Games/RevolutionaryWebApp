using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace RevolutionaryWebApp.Server.Migrations
{
    public partial class AddCIProjectModels : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ci_projects",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: false),
                    repository_full_name = table.Column<string>(type: "text", nullable: false),
                    repository_clone_url = table.Column<string>(type: "text", nullable: false),
                    project_type = table.Column<int>(type: "integer", nullable: false),
                    default_branch = table.Column<string>(type: "text", nullable: false),
                    @public = table.Column<bool>(name: "public", type: "boolean", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ci_projects", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ci_builds",
                columns: table => new
                {
                    ci_project_id = table.Column<long>(type: "bigint", nullable: false),
                    ci_build_id = table.Column<long>(type: "bigint", nullable: false),
                    commit_hash = table.Column<string>(type: "text", nullable: false),
                    remote_ref = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ci_builds", x => new { x.ci_project_id, x.ci_build_id });
                    table.ForeignKey(
                        name: "fk_ci_builds_ci_projects_ci_project_id",
                        column: x => x.ci_project_id,
                        principalTable: "ci_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ci_jobs",
                columns: table => new
                {
                    ci_project_id = table.Column<long>(type: "bigint", nullable: false),
                    ci_build_id = table.Column<long>(type: "bigint", nullable: false),
                    ci_job_id = table.Column<long>(type: "bigint", nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    job_name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ci_jobs", x => new { x.ci_project_id, x.ci_build_id, x.ci_job_id });
                    table.ForeignKey(
                        name: "fk_ci_jobs_ci_builds_ci_project_id_ci_build_id",
                        columns: x => new { x.ci_project_id, x.ci_build_id },
                        principalTable: "ci_builds",
                        principalColumns: new[] { "ci_project_id", "ci_build_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ci_job_artifacts",
                columns: table => new
                {
                    ci_project_id = table.Column<long>(type: "bigint", nullable: false),
                    ci_build_id = table.Column<long>(type: "bigint", nullable: false),
                    ci_job_id = table.Column<long>(type: "bigint", nullable: false),
                    ci_job_artifact_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    storage_item_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ci_job_artifacts", x => new { x.ci_project_id, x.ci_build_id, x.ci_job_id, x.ci_job_artifact_id });
                    table.ForeignKey(
                        name: "fk_ci_job_artifacts_ci_jobs_ci_project_id_ci_build_id_ci_job_id",
                        columns: x => new { x.ci_project_id, x.ci_build_id, x.ci_job_id },
                        principalTable: "ci_jobs",
                        principalColumns: new[] { "ci_project_id", "ci_build_id", "ci_job_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ci_job_artifacts_storage_items_storage_item_id",
                        column: x => x.storage_item_id,
                        principalTable: "storage_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ci_job_artifacts_storage_item_id",
                table: "ci_job_artifacts",
                column: "storage_item_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ci_job_artifacts");

            migrationBuilder.DropTable(
                name: "ci_jobs");

            migrationBuilder.DropTable(
                name: "ci_builds");

            migrationBuilder.DropTable(
                name: "ci_projects");
        }
    }
}
