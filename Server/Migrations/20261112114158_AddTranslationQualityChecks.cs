using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTranslationQualityChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "quality_checks",
                table: "translation_projects",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "translation_problems",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    key = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    language_code = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    message = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    ignored = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_translation_problems", x => x.id);
                    table.ForeignKey(
                        name: "fk_translation_problems_translation_values_project_id_key_lang",
                        columns: x => new { x.project_id, x.key, x.language_code },
                        principalTable: "translation_values",
                        principalColumns: new[] { "project_id", "key", "language_code" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_translation_problems_project_id_key_language_code_kind",
                table: "translation_problems",
                columns: new[] { "project_id", "key", "language_code", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_translation_problems_project_id_language_code_ignored",
                table: "translation_problems",
                columns: new[] { "project_id", "language_code", "ignored" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "translation_problems");

            migrationBuilder.DropColumn(
                name: "quality_checks",
                table: "translation_projects");
        }
    }
}
