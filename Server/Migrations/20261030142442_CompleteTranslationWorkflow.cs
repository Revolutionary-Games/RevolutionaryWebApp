using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    /// <inheritdoc />
    public partial class CompleteTranslationWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_translation_suggestion_votes_users_user_id",
                table: "translation_suggestion_votes");

            migrationBuilder.DropForeignKey(
                name: "fk_translation_suggestions_users_author_user_id",
                table: "translation_suggestions");

            migrationBuilder.AddColumn<bool>(
                name: "last_imported_fuzzy",
                table: "translation_values",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "position",
                table: "translation_units",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddForeignKey(
                name: "fk_translation_suggestion_votes_users_user_id",
                table: "translation_suggestion_votes",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_translation_suggestions_users_author_user_id",
                table: "translation_suggestions",
                column: "author_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_translation_suggestion_votes_users_user_id",
                table: "translation_suggestion_votes");

            migrationBuilder.DropForeignKey(
                name: "fk_translation_suggestions_users_author_user_id",
                table: "translation_suggestions");

            migrationBuilder.DropColumn(
                name: "last_imported_fuzzy",
                table: "translation_values");

            migrationBuilder.DropColumn(
                name: "position",
                table: "translation_units");

            migrationBuilder.AddForeignKey(
                name: "fk_translation_suggestion_votes_users_user_id",
                table: "translation_suggestion_votes",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_translation_suggestions_users_author_user_id",
                table: "translation_suggestions",
                column: "author_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
