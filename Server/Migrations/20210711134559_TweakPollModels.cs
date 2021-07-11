using Microsoft.EntityFrameworkCore.Migrations;

namespace ThriveDevCenter.Server.Migrations
{
    public partial class TweakPollModels : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "tiebreak_type",
                table: "meeting_polls",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "is_tiebreaker",
                table: "meeting_poll_votes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_meetings_read_access",
                table: "meetings",
                column: "read_access");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_meetings_read_access",
                table: "meetings");

            migrationBuilder.DropColumn(
                name: "tiebreak_type",
                table: "meeting_polls");

            migrationBuilder.DropColumn(
                name: "is_tiebreaker",
                table: "meeting_poll_votes");
        }
    }
}
