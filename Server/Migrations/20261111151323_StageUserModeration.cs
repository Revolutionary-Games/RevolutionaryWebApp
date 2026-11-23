using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    /// <inheritdoc />
    public partial class StageUserModeration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "moderation_pending",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "pending_user_moderation",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    target_user_id = table.Column<long>(type: "bigint", nullable: true),
                    actor_user_id = table.Column<long>(type: "bigint", nullable: true),
                    report_id = table.Column<long>(type: "bigint", nullable: true),
                    ban_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    purge_cutoff = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    applied_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cleaned_up_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pending_user_moderation", x => x.id);
                    table.ForeignKey(
                        name: "fk_pending_user_moderation_user_reports_report_id",
                        column: x => x.report_id,
                        principalTable: "user_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_pending_user_moderation_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_pending_user_moderation_users_target_user_id",
                        column: x => x.target_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pending_user_moderation_actor_user_id",
                table: "pending_user_moderation",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_pending_user_moderation_applied_at_created_at",
                table: "pending_user_moderation",
                columns: new[] { "applied_at", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_pending_user_moderation_cleaned_up_at_created_at",
                table: "pending_user_moderation",
                columns: new[] { "cleaned_up_at", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_pending_user_moderation_report_id",
                table: "pending_user_moderation",
                column: "report_id");

            migrationBuilder.CreateIndex(
                name: "ix_pending_user_moderation_target_user_id",
                table: "pending_user_moderation",
                column: "target_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pending_user_moderation");

            migrationBuilder.DropColumn(
                name: "moderation_pending",
                table: "users");
        }
    }
}
