using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    /// <inheritdoc />
    public partial class TranslationSoftDeletionAndSuggestionScores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "score",
                table: "translation_suggestions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "deleted",
                table: "translation_projects",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "deleted",
                table: "translation_project_languages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at",
                table: "translation_project_languages",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<bool>(
                name: "deleted",
                table: "translation_dictionaries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at",
                table: "translation_dictionaries",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<bool>(
                name: "deleted",
                table: "dictionary_terms",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "deleted",
                table: "dictionary_language_terms",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "score",
                table: "translation_suggestions");

            migrationBuilder.DropColumn(
                name: "deleted",
                table: "translation_projects");

            migrationBuilder.DropColumn(
                name: "deleted",
                table: "translation_project_languages");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "translation_project_languages");

            migrationBuilder.DropColumn(
                name: "deleted",
                table: "translation_dictionaries");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "translation_dictionaries");

            migrationBuilder.DropColumn(
                name: "deleted",
                table: "dictionary_terms");

            migrationBuilder.DropColumn(
                name: "deleted",
                table: "dictionary_language_terms");
        }
    }
}
