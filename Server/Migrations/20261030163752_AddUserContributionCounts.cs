using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddUserContributionCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_contribution_counts",
                columns: table => new
                {
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    contribution_limit = table.Column<int>(type: "integer", nullable: false),
                    current_contribution = table.Column<int>(type: "integer", nullable: false),
                    contribution_exceeded = table.Column<bool>(type: "boolean", nullable: false),
                    first_contributed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_contribution_counts", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_user_contribution_counts_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_contribution_counts");
        }
    }
}
