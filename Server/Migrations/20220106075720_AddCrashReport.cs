using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace RevolutionaryWebApp.Server.Migrations
{
    public partial class AddCrashReport : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "crash_reports",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    @public = table.Column<bool>(name: "public", type: "boolean", nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    platform = table.Column<int>(type: "integer", nullable: false),
                    happened_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    delete_key = table.Column<Guid>(type: "uuid", nullable: false),
                    hashed_delete_key = table.Column<string>(type: "text", nullable: false),
                    uploaded_from = table.Column<IPAddress>(type: "inet", nullable: false),
                    exit_code_or_signal = table.Column<string>(type: "text", nullable: false),
                    logs = table.Column<string>(type: "text", nullable: true),
                    store = table.Column<string>(type: "text", nullable: true),
                    version = table.Column<string>(type: "text", nullable: true),
                    primary_callstack = table.Column<string>(type: "text", nullable: true),
                    whole_crash_dump = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    description_last_edited = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    description_last_edited_by_id = table.Column<long>(type: "bigint", nullable: true),
                    duplicate_of_id = table.Column<long>(type: "bigint", nullable: true),
                    dump_local_file_name = table.Column<string>(type: "text", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_crash_reports", x => x.id);
                    table.ForeignKey(
                        name: "fk_crash_reports_crash_reports_duplicate_of_id",
                        column: x => x.duplicate_of_id,
                        principalTable: "crash_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_crash_reports_users_description_last_edited_by_id",
                        column: x => x.description_last_edited_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_crash_reports_created_at",
                table: "crash_reports",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_crash_reports_description_last_edited_by_id",
                table: "crash_reports",
                column: "description_last_edited_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_crash_reports_duplicate_of_id",
                table: "crash_reports",
                column: "duplicate_of_id");

            migrationBuilder.CreateIndex(
                name: "ix_crash_reports_happened_at",
                table: "crash_reports",
                column: "happened_at");

            migrationBuilder.CreateIndex(
                name: "ix_crash_reports_hashed_delete_key",
                table: "crash_reports",
                column: "hashed_delete_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_crash_reports_updated_at",
                table: "crash_reports",
                column: "updated_at");

            migrationBuilder.CreateIndex(
                name: "ix_crash_reports_uploaded_from",
                table: "crash_reports",
                column: "uploaded_from");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "crash_reports");
        }
    }
}
