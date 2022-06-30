using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThriveDevCenter.Server.Migrations
{
    public partial class AddNewMeetingPollFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "chairman_id",
                table: "meetings",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "manually_closed_by_id",
                table: "meeting_polls",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_meetings_chairman_id",
                table: "meetings",
                column: "chairman_id");

            migrationBuilder.CreateIndex(
                name: "ix_meeting_polls_manually_closed_by_id",
                table: "meeting_polls",
                column: "manually_closed_by_id");

            migrationBuilder.AddForeignKey(
                name: "fk_meeting_polls_users_manually_closed_by_id",
                table: "meeting_polls",
                column: "manually_closed_by_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_meetings_users_chairman_id",
                table: "meetings",
                column: "chairman_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_meeting_polls_users_manually_closed_by_id",
                table: "meeting_polls");

            migrationBuilder.DropForeignKey(
                name: "fk_meetings_users_chairman_id",
                table: "meetings");

            migrationBuilder.DropIndex(
                name: "ix_meetings_chairman_id",
                table: "meetings");

            migrationBuilder.DropIndex(
                name: "ix_meeting_polls_manually_closed_by_id",
                table: "meeting_polls");

            migrationBuilder.DropColumn(
                name: "chairman_id",
                table: "meetings");

            migrationBuilder.DropColumn(
                name: "manually_closed_by_id",
                table: "meeting_polls");
        }
    }
}
