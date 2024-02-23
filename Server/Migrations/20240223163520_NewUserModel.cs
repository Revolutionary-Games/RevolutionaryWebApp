using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    /// <inheritdoc />
    public partial class NewUserModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_meeting_poll_votes_meeting_polls_poll_temp_id",
                table: "meeting_poll_votes");

            migrationBuilder.DropForeignKey(
                name: "fk_meeting_poll_voting_records_meeting_polls_poll_temp_id1",
                table: "meeting_poll_voting_records");

            migrationBuilder.DropForeignKey(
                name: "fk_storage_item_delete_infos_storage_items_storage_item_id1",
                table: "storage_item_delete_infos");

            migrationBuilder.DropColumn(
                name: "access_failed_count",
                table: "users");

            migrationBuilder.DropColumn(
                name: "concurrency_stamp",
                table: "users");

            migrationBuilder.DropColumn(
                name: "email_confirmed",
                table: "users");

            migrationBuilder.DropColumn(
                name: "lockout_enabled",
                table: "users");

            migrationBuilder.DropColumn(
                name: "lockout_end",
                table: "users");

            migrationBuilder.DropColumn(
                name: "normalized_user_name",
                table: "users");

            migrationBuilder.DropColumn(
                name: "phone_number",
                table: "users");

            migrationBuilder.DropColumn(
                name: "phone_number_confirmed",
                table: "users");

            migrationBuilder.DropColumn(
                name: "security_stamp",
                table: "users");

            migrationBuilder.DropColumn(
                name: "two_factor_enabled",
                table: "users");

            migrationBuilder.AlterColumn<bool>(
                name: "suspended_manually",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldNullable: true,
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<bool>(
                name: "suspended",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldNullable: true,
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "users",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValue: "UNKNOWN");

            migrationBuilder.AddColumn<string>(
                name: "display_name",
                table: "users",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_display_name",
                table: "users",
                column: "display_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_normalized_email",
                table: "users",
                column: "normalized_email",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_meeting_poll_votes_meeting_polls_meeting_id_poll_id",
                table: "meeting_poll_votes",
                columns: new[] { "meeting_id", "poll_id" },
                principalTable: "meeting_polls",
                principalColumns: new[] { "meeting_id", "poll_id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_meeting_poll_voting_records_meeting_polls_meeting_id_poll_id",
                table: "meeting_poll_voting_records",
                columns: new[] { "meeting_id", "poll_id" },
                principalTable: "meeting_polls",
                principalColumns: new[] { "meeting_id", "poll_id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_storage_item_delete_infos_storage_items_storage_item_id",
                table: "storage_item_delete_infos",
                column: "storage_item_id",
                principalTable: "storage_items",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_meeting_poll_votes_meeting_polls_meeting_id_poll_id",
                table: "meeting_poll_votes");

            migrationBuilder.DropForeignKey(
                name: "fk_meeting_poll_voting_records_meeting_polls_meeting_id_poll_id",
                table: "meeting_poll_voting_records");

            migrationBuilder.DropForeignKey(
                name: "fk_storage_item_delete_infos_storage_items_storage_item_id",
                table: "storage_item_delete_infos");

            migrationBuilder.DropIndex(
                name: "ix_users_display_name",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_users_normalized_email",
                table: "users");

            migrationBuilder.DropColumn(
                name: "display_name",
                table: "users");

            migrationBuilder.AlterColumn<bool>(
                name: "suspended_manually",
                table: "users",
                type: "boolean",
                nullable: true,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<bool>(
                name: "suspended",
                table: "users",
                type: "boolean",
                nullable: true,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "users",
                type: "text",
                nullable: false,
                defaultValue: "UNKNOWN",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<int>(
                name: "access_failed_count",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "concurrency_stamp",
                table: "users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "email_confirmed",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "lockout_enabled",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "lockout_end",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "normalized_user_name",
                table: "users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "phone_number",
                table: "users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "phone_number_confirmed",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "security_stamp",
                table: "users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "two_factor_enabled",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddForeignKey(
                name: "fk_meeting_poll_votes_meeting_polls_poll_temp_id",
                table: "meeting_poll_votes",
                columns: new[] { "meeting_id", "poll_id" },
                principalTable: "meeting_polls",
                principalColumns: new[] { "meeting_id", "poll_id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_meeting_poll_voting_records_meeting_polls_poll_temp_id1",
                table: "meeting_poll_voting_records",
                columns: new[] { "meeting_id", "poll_id" },
                principalTable: "meeting_polls",
                principalColumns: new[] { "meeting_id", "poll_id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_storage_item_delete_infos_storage_items_storage_item_id1",
                table: "storage_item_delete_infos",
                column: "storage_item_id",
                principalTable: "storage_items",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
