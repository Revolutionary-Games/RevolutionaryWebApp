using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThriveDevCenter.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddWatchedKeywords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "watched_keywords",
                columns: table => new
                {
                    keyword = table.Column<string>(type: "text", nullable: false),
                    lastseen = table.Column<DateTime>(name: "last_seen", type: "timestamp with time zone", nullable: false),
                    totalcount = table.Column<int>(name: "total_count", type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_watched_keywords", x => x.keyword);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "watched_keywords");
        }
    }
}
