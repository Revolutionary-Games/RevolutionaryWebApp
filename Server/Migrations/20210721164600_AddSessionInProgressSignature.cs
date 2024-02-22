using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace RevolutionaryWebApp.Server.Migrations
{
    public partial class AddSessionInProgressSignature : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "in_progress_cla_signatures",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "text", nullable: true),
                    email_verified = table.Column<bool>(type: "boolean", nullable: false),
                    email_verification_code = table.Column<Guid>(type: "uuid", nullable: true),
                    github_account = table.Column<string>(type: "text", nullable: true),
                    developer_username = table.Column<string>(type: "text", nullable: true),
                    signer_name = table.Column<string>(type: "text", nullable: true),
                    signer_signature = table.Column<string>(type: "text", nullable: true),
                    signer_is_minor = table.Column<bool>(type: "boolean", nullable: false),
                    guardian_name = table.Column<string>(type: "text", nullable: true),
                    guardian_signature = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_in_progress_cla_signatures", x => x.id);
                    table.ForeignKey(
                        name: "fk_in_progress_cla_signatures_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_in_progress_cla_signatures_email_verification_code",
                table: "in_progress_cla_signatures",
                column: "email_verification_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_in_progress_cla_signatures_session_id",
                table: "in_progress_cla_signatures",
                column: "session_id",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "in_progress_cla_signatures");
        }
    }
}
