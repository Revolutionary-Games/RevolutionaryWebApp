using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddSignupModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "email_bounces",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    email = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    outstanding_bounces = table.Column<int>(type: "integer", nullable: false),
                    first_bounce_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_bounce_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    disabled_by_system = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    backoff_weeks = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_bounces", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pending_user_signups",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    email = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_email_sent_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    send_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pending_user_signups", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_email_bounces_email",
                table: "email_bounces",
                column: "email");

            migrationBuilder.CreateIndex(
                name: "ix_email_bounces_normalized_email",
                table: "email_bounces",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "ix_pending_user_signups_normalized_email",
                table: "pending_user_signups",
                column: "normalized_email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_bounces");

            migrationBuilder.DropTable(
                name: "pending_user_signups");
        }
    }
}
