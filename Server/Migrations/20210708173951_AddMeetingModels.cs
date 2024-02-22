using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace RevolutionaryWebApp.Server.Migrations
{
    public partial class AddMeetingModels : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "association_member",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "board_member",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "has_been_board_member",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "clas",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "meetings",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: false),
                    minutes = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: false),
                    starts_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    expected_duration = table.Column<TimeSpan>(type: "interval", nullable: true),
                    join_grace_period = table.Column<TimeSpan>(type: "interval", nullable: false),
                    ended_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    read_access = table.Column<int>(type: "integer", nullable: false),
                    join_access = table.Column<int>(type: "integer", nullable: false),
                    read_only = table.Column<bool>(type: "boolean", nullable: false),
                    owner_id = table.Column<long>(type: "bigint", nullable: true),
                    secretary_id = table.Column<long>(type: "bigint", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meetings", x => x.id);
                    table.ForeignKey(
                        name: "fk_meetings_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_meetings_users_secretary_id",
                        column: x => x.secretary_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "meeting_members",
                columns: table => new
                {
                    meeting_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    joined_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    can_review_minutes = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meeting_members", x => new { x.meeting_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_meeting_members_meetings_meeting_id",
                        column: x => x.meeting_id,
                        principalTable: "meetings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_meeting_members_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "meeting_polls",
                columns: table => new
                {
                    meeting_id = table.Column<long>(type: "bigint", nullable: false),
                    poll_id = table.Column<long>(type: "bigint", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    poll_data = table.Column<string>(type: "text", nullable: false),
                    poll_results = table.Column<string>(type: "text", nullable: true),
                    poll_results_created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    closed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    auto_close_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meeting_polls", x => new { x.meeting_id, x.poll_id });
                    table.ForeignKey(
                        name: "fk_meeting_polls_meetings_meeting_id",
                        column: x => x.meeting_id,
                        principalTable: "meetings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "meeting_poll_votes",
                columns: table => new
                {
                    vote_id = table.Column<Guid>(type: "uuid", nullable: false),
                    meeting_id = table.Column<long>(type: "bigint", nullable: false),
                    poll_id = table.Column<long>(type: "bigint", nullable: false),
                    voting_power = table.Column<float>(type: "real", nullable: false),
                    vote_content = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meeting_poll_votes", x => x.vote_id);
                    table.ForeignKey(
                        name: "fk_meeting_poll_votes_meeting_polls_poll_meeting_id_poll_id1",
                        columns: x => new { x.meeting_id, x.poll_id },
                        principalTable: "meeting_polls",
                        principalColumns: new[] { "meeting_id", "poll_id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_meeting_poll_votes_meetings_meeting_id",
                        column: x => x.meeting_id,
                        principalTable: "meetings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "meeting_poll_voting_records",
                columns: table => new
                {
                    meeting_id = table.Column<long>(type: "bigint", nullable: false),
                    poll_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meeting_poll_voting_records", x => new { x.meeting_id, x.poll_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_meeting_poll_voting_records_meeting_polls_poll_meeting_id_p",
                        columns: x => new { x.meeting_id, x.poll_id },
                        principalTable: "meeting_polls",
                        principalColumns: new[] { "meeting_id", "poll_id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_meeting_poll_voting_records_meetings_meeting_id",
                        column: x => x.meeting_id,
                        principalTable: "meetings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_meeting_poll_voting_records_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_meeting_members_user_id",
                table: "meeting_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_meeting_poll_votes_meeting_id_poll_id",
                table: "meeting_poll_votes",
                columns: new[] { "meeting_id", "poll_id" });

            migrationBuilder.CreateIndex(
                name: "ix_meeting_poll_voting_records_user_id",
                table: "meeting_poll_voting_records",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_meeting_polls_meeting_id_title",
                table: "meeting_polls",
                columns: new[] { "meeting_id", "title" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_meetings_name",
                table: "meetings",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_meetings_owner_id",
                table: "meetings",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_meetings_secretary_id",
                table: "meetings",
                column: "secretary_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "meeting_members");

            migrationBuilder.DropTable(
                name: "meeting_poll_votes");

            migrationBuilder.DropTable(
                name: "meeting_poll_voting_records");

            migrationBuilder.DropTable(
                name: "meeting_polls");

            migrationBuilder.DropTable(
                name: "meetings");

            migrationBuilder.DropColumn(
                name: "association_member",
                table: "users");

            migrationBuilder.DropColumn(
                name: "board_member",
                table: "users");

            migrationBuilder.DropColumn(
                name: "has_been_board_member",
                table: "users");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "clas");
        }
    }
}
