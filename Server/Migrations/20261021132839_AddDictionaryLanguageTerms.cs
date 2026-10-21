using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddDictionaryLanguageTerms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dictionary_language_terms",
                columns: table => new
                {
                    dictionary_id = table.Column<long>(type: "bigint", nullable: false),
                    language_code = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: false),
                    word = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    context = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    explanation = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    last_edited_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dictionary_language_terms", x => new { x.dictionary_id, x.language_code, x.word, x.context });
                    table.ForeignKey(
                        name: "fk_dictionary_language_terms_translation_dictionaries_dictiona",
                        column: x => x.dictionary_id,
                        principalTable: "translation_dictionaries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dictionary_language_terms_users_last_edited_by_user_id",
                        column: x => x.last_edited_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_dictionary_language_terms_last_edited_by_user_id",
                table: "dictionary_language_terms",
                column: "last_edited_by_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dictionary_language_terms");
        }
    }
}
