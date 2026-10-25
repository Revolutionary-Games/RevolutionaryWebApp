using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddDictionaryEntryCreationTimes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "created_at",
                table: "dictionary_terms",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "timezone('utc', now())");

            migrationBuilder.AddColumn<DateTime>(
                name: "created_at",
                table: "dictionary_term_translations",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "timezone('utc', now())");

            migrationBuilder.AddColumn<DateTime>(
                name: "created_at",
                table: "dictionary_language_terms",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "timezone('utc', now())");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "created_at",
                table: "dictionary_terms");

            migrationBuilder.DropColumn(
                name: "created_at",
                table: "dictionary_term_translations");

            migrationBuilder.DropColumn(
                name: "created_at",
                table: "dictionary_language_terms");
        }
    }
}
