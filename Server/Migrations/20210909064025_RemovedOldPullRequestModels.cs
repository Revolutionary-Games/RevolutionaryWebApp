using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace RevolutionaryWebApp.Server.Migrations
{
    public partial class RemovedOldPullRequestModels : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pull_request_auto_comments");

            migrationBuilder.DropTable(
                name: "pull_requests");

            migrationBuilder.CreateIndex(
                name: "ix_github_pull_requests_repository_github_id",
                table: "github_pull_requests",
                columns: new[] { "repository", "github_id" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_github_pull_requests_repository_github_id",
                table: "github_pull_requests");

            migrationBuilder.CreateTable(
                name: "pull_request_auto_comments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    comment = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    when = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pull_request_auto_comments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pull_requests",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    cla_status = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    posted_comments_raw = table.Column<string>(type: "text", nullable: true),
                    pull_request_identification = table.Column<string>(type: "text", nullable: false),
                    repository = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pull_requests", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pull_requests_repository_pull_request_identification",
                table: "pull_requests",
                columns: new[] { "repository", "pull_request_identification" },
                unique: true);
        }
    }
}
