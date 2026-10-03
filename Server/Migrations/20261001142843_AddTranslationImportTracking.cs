using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTranslationImportTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "last_imported_text",
                table: "translation_values",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "translation_import_conflicts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    key = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    language_code = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: false),
                    local_text = table.Column<string>(type: "text", nullable: false),
                    incoming_text = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_translation_import_conflicts", x => x.id);
                    table.ForeignKey(
                        name: "fk_translation_import_conflicts_translation_values_project_id_",
                        columns: x => new { x.project_id, x.key, x.language_code },
                        principalTable: "translation_values",
                        principalColumns: new[] { "project_id", "key", "language_code" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_translation_import_conflicts_project_id_key_language_code_r",
                table: "translation_import_conflicts",
                columns: new[] { "project_id", "key", "language_code", "resolved_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "translation_import_conflicts");

            migrationBuilder.DropColumn(
                name: "last_imported_text",
                table: "translation_values");
        }
    }
}
