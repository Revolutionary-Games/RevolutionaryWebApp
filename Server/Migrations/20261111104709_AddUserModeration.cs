using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddUserModeration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "last_known_ip_address",
                table: "users",
                type: "character varying(45)",
                maxLength: 45,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "muted_until",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "created_by_user_id",
                table: "translation_units",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "created_at",
                table: "translation_suggestion_votes",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.AddColumn<bool>(
                name: "reported",
                table: "translation_comments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "new_explanation",
                table: "translation_changes",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "old_explanation",
                table: "translation_changes",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "reverted_at",
                table: "translation_changes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "moderation_revisions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: true),
                    entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    key_json = table.Column<string>(type: "text", nullable: false),
                    before_json = table.Column<string>(type: "text", nullable: false),
                    after_json = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    reverted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_revisions", x => x.id);
                    table.ForeignKey(
                        name: "fk_moderation_revisions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "moderation_upload_deletions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    path = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    storage = table.Column<int>(type: "integer", nullable: false),
                    delete_after = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_moderation_upload_deletions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "registration_blocks",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    target_user_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_registration_blocks", x => x.id);
                    table.ForeignKey(
                        name: "fk_registration_blocks_users_target_user_id",
                        column: x => x.target_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "user_reports",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    reporter_user_id = table.Column<long>(type: "bigint", nullable: true),
                    target_user_id = table.Column<long>(type: "bigint", nullable: true),
                    target_user_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    target = table.Column<int>(type: "integer", nullable: false),
                    target_id = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<int>(type: "integer", nullable: false),
                    details = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    content_snapshot = table.Column<string>(type: "character varying(50000)", maxLength: 50000, nullable: false),
                    content_path = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    resolved_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    resolution = table.Column<int>(type: "integer", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_reports", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_reports_users_reporter_user_id",
                        column: x => x.reporter_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_user_reports_users_resolved_by_user_id",
                        column: x => x.resolved_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_user_reports_users_target_user_id",
                        column: x => x.target_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "user_report_notes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    report_id = table.Column<long>(type: "bigint", nullable: false),
                    author_user_id = table.Column<long>(type: "bigint", nullable: true),
                    text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_report_notes", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_report_notes_user_reports_report_id",
                        column: x => x.report_id,
                        principalTable: "user_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_report_notes_users_author_user_id",
                        column: x => x.author_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_translation_units_created_by_user_id",
                table: "translation_units",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_moderation_revisions_user_id_created_at",
                table: "moderation_revisions",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_moderation_upload_deletions_completed_at_delete_after",
                table: "moderation_upload_deletions",
                columns: new[] { "completed_at", "delete_after" });

            migrationBuilder.CreateIndex(
                name: "ix_registration_blocks_ip_address",
                table: "registration_blocks",
                column: "ip_address");

            migrationBuilder.CreateIndex(
                name: "ix_registration_blocks_normalized_email",
                table: "registration_blocks",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "ix_registration_blocks_target_user_id",
                table: "registration_blocks",
                column: "target_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_report_notes_author_user_id",
                table: "user_report_notes",
                column: "author_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_report_notes_report_id",
                table: "user_report_notes",
                column: "report_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_reports_reporter_user_id",
                table: "user_reports",
                column: "reporter_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_reports_resolved_at_created_at",
                table: "user_reports",
                columns: new[] { "resolved_at", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_user_reports_resolved_by_user_id",
                table: "user_reports",
                column: "resolved_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_reports_target_target_id_resolved_at",
                table: "user_reports",
                columns: new[] { "target", "target_id", "resolved_at" });

            migrationBuilder.CreateIndex(
                name: "ix_user_reports_target_user_id",
                table: "user_reports",
                column: "target_user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_translation_units_users_created_by_user_id",
                table: "translation_units",
                column: "created_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_translation_units_users_created_by_user_id",
                table: "translation_units");

            migrationBuilder.DropTable(
                name: "moderation_revisions");

            migrationBuilder.DropTable(
                name: "moderation_upload_deletions");

            migrationBuilder.DropTable(
                name: "registration_blocks");

            migrationBuilder.DropTable(
                name: "user_report_notes");

            migrationBuilder.DropTable(
                name: "user_reports");

            migrationBuilder.DropIndex(
                name: "ix_translation_units_created_by_user_id",
                table: "translation_units");

            migrationBuilder.DropColumn(
                name: "last_known_ip_address",
                table: "users");

            migrationBuilder.DropColumn(
                name: "muted_until",
                table: "users");

            migrationBuilder.DropColumn(
                name: "created_by_user_id",
                table: "translation_units");

            migrationBuilder.DropColumn(
                name: "created_at",
                table: "translation_suggestion_votes");

            migrationBuilder.DropColumn(
                name: "reported",
                table: "translation_comments");

            migrationBuilder.DropColumn(
                name: "new_explanation",
                table: "translation_changes");

            migrationBuilder.DropColumn(
                name: "old_explanation",
                table: "translation_changes");

            migrationBuilder.DropColumn(
                name: "reverted_at",
                table: "translation_changes");
        }
    }
}
