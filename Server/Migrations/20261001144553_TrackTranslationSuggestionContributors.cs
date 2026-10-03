using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    /// <inheritdoc />
    public partial class TrackTranslationSuggestionContributors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "resolved_by_user_id",
                table: "translation_suggestions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "contributor_user_id",
                table: "translation_changes",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_translation_suggestions_resolved_by_user_id",
                table: "translation_suggestions",
                column: "resolved_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_translation_changes_contributor_user_id",
                table: "translation_changes",
                column: "contributor_user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_translation_changes_users_contributor_user_id",
                table: "translation_changes",
                column: "contributor_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_translation_suggestions_users_resolved_by_user_id",
                table: "translation_suggestions",
                column: "resolved_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_translation_changes_users_contributor_user_id",
                table: "translation_changes");

            migrationBuilder.DropForeignKey(
                name: "fk_translation_suggestions_users_resolved_by_user_id",
                table: "translation_suggestions");

            migrationBuilder.DropIndex(
                name: "ix_translation_suggestions_resolved_by_user_id",
                table: "translation_suggestions");

            migrationBuilder.DropIndex(
                name: "ix_translation_changes_contributor_user_id",
                table: "translation_changes");

            migrationBuilder.DropColumn(
                name: "resolved_by_user_id",
                table: "translation_suggestions");

            migrationBuilder.DropColumn(
                name: "contributor_user_id",
                table: "translation_changes");
        }
    }
}
