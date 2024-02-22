using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace RevolutionaryWebApp.Server.Migrations
{
    public partial class AddGithubAutoComment : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "github_auto_comments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    repository = table.Column<string>(type: "text", nullable: true),
                    comment_text = table.Column<string>(type: "text", nullable: false),
                    condition = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_github_auto_comments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "github_pull_requests",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    repository = table.Column<string>(type: "text", nullable: false),
                    github_id = table.Column<long>(type: "bigint", nullable: false),
                    open = table.Column<bool>(type: "boolean", nullable: false),
                    author_username = table.Column<string>(type: "text", nullable: false),
                    latest_commit = table.Column<string>(type: "text", nullable: false),
                    cla_signed = table.Column<bool>(type: "boolean", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_github_pull_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "github_auto_comment_github_pull_request",
                columns: table => new
                {
                    auto_comments_id = table.Column<long>(type: "bigint", nullable: false),
                    posted_on_pull_requests_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_github_auto_comment_github_pull_request", x => new { x.auto_comments_id, x.posted_on_pull_requests_id });
                    table.ForeignKey(
                        name: "fk_github_auto_comment_github_pull_request_github_auto_comment",
                        column: x => x.auto_comments_id,
                        principalTable: "github_auto_comments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_github_auto_comment_github_pull_request_github_pull_request",
                        column: x => x.posted_on_pull_requests_id,
                        principalTable: "github_pull_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_github_auto_comment_github_pull_request_posted_on_pull_requ",
                table: "github_auto_comment_github_pull_request",
                column: "posted_on_pull_requests_id");

            migrationBuilder.CreateIndex(
                name: "ix_github_auto_comments_condition",
                table: "github_auto_comments",
                column: "condition");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "github_auto_comment_github_pull_request");

            migrationBuilder.DropTable(
                name: "github_auto_comments");

            migrationBuilder.DropTable(
                name: "github_pull_requests");
        }
    }
}
