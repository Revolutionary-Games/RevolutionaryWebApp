using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    /// <inheritdoc />
    public partial class SeparateAPIKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_hashed_api_token",
                table: "users");

            migrationBuilder.DropColumn(
                name: "api_token",
                table: "users");

            migrationBuilder.DropColumn(
                name: "hashed_api_token",
                table: "users");

            migrationBuilder.CreateTable(
                name: "api_keys",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    secret = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_used_ip = table.Column<string>(type: "text", nullable: true),
                    total_uses = table.Column<long>(type: "bigint", nullable: false),
                    allowed_ip_network = table.Column<string>(type: "text", nullable: true),
                    scopes = table.Column<string>(type: "text", nullable: false),
                    read_only = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_keys", x => x.id);
                    table.ForeignKey(
                        name: "fk_api_keys_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_user_id",
                table: "api_keys",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "api_keys");

            migrationBuilder.AddColumn<string>(
                name: "api_token",
                table: "users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "hashed_api_token",
                table: "users",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_hashed_api_token",
                table: "users",
                column: "hashed_api_token",
                unique: true);
        }
    }
}
