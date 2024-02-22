using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace RevolutionaryWebApp.Server.Migrations
{
    public partial class AddSentBulkEmail : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sent_bulk_emails",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    title = table.Column<string>(type: "text", nullable: false),
                    recipients = table.Column<int>(type: "integer", nullable: false),
                    sent_by_id = table.Column<long>(type: "bigint", nullable: true),
                    system_send = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sent_bulk_emails", x => x.id);
                    table.ForeignKey(
                        name: "fk_sent_bulk_emails_users_sent_by_id",
                        column: x => x.sent_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sent_bulk_emails_created_at",
                table: "sent_bulk_emails",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_sent_bulk_emails_sent_by_id",
                table: "sent_bulk_emails",
                column: "sent_by_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sent_bulk_emails");
        }
    }
}
