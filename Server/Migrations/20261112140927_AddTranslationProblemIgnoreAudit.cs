using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTranslationProblemIgnoreAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ignored_at",
                table: "translation_problems",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ignored_by_user_id",
                table: "translation_problems",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_translation_problems_ignored_by_user_id",
                table: "translation_problems",
                column: "ignored_by_user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_translation_problems_users_ignored_by_user_id",
                table: "translation_problems",
                column: "ignored_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_translation_problems_users_ignored_by_user_id",
                table: "translation_problems");

            migrationBuilder.DropIndex(
                name: "ix_translation_problems_ignored_by_user_id",
                table: "translation_problems");

            migrationBuilder.DropColumn(
                name: "ignored_at",
                table: "translation_problems");

            migrationBuilder.DropColumn(
                name: "ignored_by_user_id",
                table: "translation_problems");
        }
    }
}
