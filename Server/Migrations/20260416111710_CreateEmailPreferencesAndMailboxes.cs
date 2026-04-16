using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    /// <inheritdoc />
    public partial class CreateEmailPreferencesAndMailboxes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "direct_email_preferences",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    email = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    disable_all_emails = table.Column<bool>(type: "boolean", nullable: false),
                    allow_site_announcement = table.Column<bool>(type: "boolean", nullable: false),
                    allow_password_reset = table.Column<bool>(type: "boolean", nullable: false),
                    allow_confirm_email = table.Column<bool>(type: "boolean", nullable: false),
                    allow_notifications = table.Column<bool>(type: "boolean", nullable: false),
                    allow_push_build_status = table.Column<bool>(type: "boolean", nullable: false),
                    allow_commit_build_status = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_direct_email_preferences", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mailboxes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    username = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    password = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    disabled = table.Column<bool>(type: "boolean", nullable: false),
                    last_clean_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_read_email_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_received_email_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mailboxes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_email_preferences",
                columns: table => new
                {
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    disable_all_emails = table.Column<bool>(type: "boolean", nullable: false),
                    allow_site_announcement = table.Column<bool>(type: "boolean", nullable: false),
                    allow_password_reset = table.Column<bool>(type: "boolean", nullable: false),
                    allow_confirm_email = table.Column<bool>(type: "boolean", nullable: false),
                    allow_notifications = table.Column<bool>(type: "boolean", nullable: false),
                    allow_push_build_status = table.Column<bool>(type: "boolean", nullable: false),
                    allow_commit_build_status = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_email_preferences", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_user_email_preferences_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "mailboxes",
                columns: new[] { "id", "disabled", "last_clean_utc", "last_read_email_utc", "last_received_email_utc", "name", "password", "username" },
                values: new object[] { 1L, false, null, null, null, "NotificationsReply", null, null });

            migrationBuilder.CreateIndex(
                name: "ix_direct_email_preferences_email",
                table: "direct_email_preferences",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_direct_email_preferences_normalized_email",
                table: "direct_email_preferences",
                column: "normalized_email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "direct_email_preferences");

            migrationBuilder.DropTable(
                name: "mailboxes");

            migrationBuilder.DropTable(
                name: "user_email_preferences");
        }
    }
}
