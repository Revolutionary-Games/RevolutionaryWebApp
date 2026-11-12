using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTwoFactorAuthentication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "pending_two_factor_expires_at",
                table: "sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "pending_two_factor_key_version",
                table: "sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pending_two_factor_return_url",
                table: "sessions",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "pending_two_factor_user_id",
                table: "sessions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "two_factor_verified_at",
                table: "sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "two_factor_authentications",
                columns: table => new
                {
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    protected_secret = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    setup_expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    backup_code_hashes = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    last_used_time_step = table.Column<long>(type: "bigint", nullable: true),
                    failed_attempts = table.Column<int>(type: "integer", nullable: false),
                    locked_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    recovery_requested_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    recovery_warnings_sent = table.Column<int>(type: "integer", nullable: false),
                    last_recovery_warning_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    key_version = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_two_factor_authentications", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_two_factor_authentications_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sessions_pending_two_factor_user_id",
                table: "sessions",
                column: "pending_two_factor_user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_sessions_users_pending_two_factor_user_id",
                table: "sessions",
                column: "pending_two_factor_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_sessions_users_pending_two_factor_user_id",
                table: "sessions");

            migrationBuilder.DropTable(
                name: "two_factor_authentications");

            migrationBuilder.DropIndex(
                name: "ix_sessions_pending_two_factor_user_id",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "pending_two_factor_expires_at",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "pending_two_factor_key_version",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "pending_two_factor_return_url",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "pending_two_factor_user_id",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "two_factor_verified_at",
                table: "sessions");
        }
    }
}
