using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    /// <inheritdoc />
    public partial class TranslationAnnouncements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "announcement_ends_at",
                table: "translation_projects",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "announcement_message",
                table: "translation_projects",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "announcement_severity",
                table: "translation_projects",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "announcement_ends_at",
                table: "translation_projects");

            migrationBuilder.DropColumn(
                name: "announcement_message",
                table: "translation_projects");

            migrationBuilder.DropColumn(
                name: "announcement_severity",
                table: "translation_projects");
        }
    }
}
