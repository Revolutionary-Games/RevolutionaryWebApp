using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace RevolutionaryWebApp.Server.Migrations
{
    public partial class AddCLAModels : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "clas",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    raw_markdown = table.Column<string>(type: "text", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pull_request_auto_comments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    comment = table.Column<string>(type: "text", nullable: false),
                    when = table.Column<int>(type: "integer", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
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
                    repository = table.Column<string>(type: "text", nullable: false),
                    pull_request_identification = table.Column<string>(type: "text", nullable: false),
                    cla_status = table.Column<int>(type: "integer", nullable: false),
                    posted_comments_raw = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pull_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cla_signatures",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    valid_until = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    email = table.Column<string>(type: "text", nullable: false),
                    github_account = table.Column<string>(type: "text", nullable: true),
                    developer_username = table.Column<string>(type: "text", nullable: true),
                    cla_signature_storage_path = table.Column<string>(type: "text", nullable: false),
                    cla_invalidation_storage_path = table.Column<string>(type: "text", nullable: true),
                    cla_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cla_signatures", x => x.id);
                    table.ForeignKey(
                        name: "fk_cla_signatures_clas_cla_id",
                        column: x => x.cla_id,
                        principalTable: "clas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cla_signatures_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cla_signatures_cla_id_email",
                table: "cla_signatures",
                columns: new[] { "cla_id", "email" });

            migrationBuilder.CreateIndex(
                name: "ix_cla_signatures_cla_id_github_account",
                table: "cla_signatures",
                columns: new[] { "cla_id", "github_account" });

            migrationBuilder.CreateIndex(
                name: "ix_cla_signatures_cla_invalidation_storage_path",
                table: "cla_signatures",
                column: "cla_invalidation_storage_path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cla_signatures_cla_signature_storage_path",
                table: "cla_signatures",
                column: "cla_signature_storage_path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cla_signatures_user_id",
                table: "cla_signatures",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_clas_active",
                table: "clas",
                column: "active");

            migrationBuilder.CreateIndex(
                name: "ix_pull_requests_repository_pull_request_identification",
                table: "pull_requests",
                columns: new[] { "repository", "pull_request_identification" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cla_signatures");

            migrationBuilder.DropTable(
                name: "pull_request_auto_comments");

            migrationBuilder.DropTable(
                name: "pull_requests");

            migrationBuilder.DropTable(
                name: "clas");
        }
    }
}
