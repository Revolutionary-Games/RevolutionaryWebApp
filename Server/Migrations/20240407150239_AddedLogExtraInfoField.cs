using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddedLogExtraInfoField : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "permalink",
                table: "versioned_pages",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "reverse_diff",
                table: "page_versions",
                type: "character varying(2129920)",
                maxLength: 2129920,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(2098176)",
                oldMaxLength: 2098176);

            migrationBuilder.AddColumn<string>(
                name: "extended",
                table: "log_entries",
                type: "character varying(1048576)",
                maxLength: 1048576,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "extended",
                table: "admin_actions",
                type: "character varying(1048576)",
                maxLength: 1048576,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "extended",
                table: "action_log_entries",
                type: "character varying(1048576)",
                maxLength: 1048576,
                nullable: true);

            migrationBuilder.InsertData(
                table: "user_groups",
                columns: new[] { "id", "name" },
                values: new object[] { 10, "SiteLayoutPublisher" });

            migrationBuilder.InsertData(
                table: "user_groups_extra_data",
                columns: new[] { "group_id", "created_at", "custom_description", "updated_at" },
                values: new object[] { 10, new DateTime(2023, 4, 2, 16, 4, 31, 355, DateTimeKind.Utc).AddTicks(4672), "Inbuilt group, cannot be modified", new DateTime(2023, 4, 2, 16, 4, 31, 355, DateTimeKind.Utc).AddTicks(4672) });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "user_groups_extra_data",
                keyColumn: "group_id",
                keyValue: 10);

            migrationBuilder.DeleteData(
                table: "user_groups",
                keyColumn: "id",
                keyValue: 10);

            migrationBuilder.DropColumn(
                name: "extended",
                table: "log_entries");

            migrationBuilder.DropColumn(
                name: "extended",
                table: "admin_actions");

            migrationBuilder.DropColumn(
                name: "extended",
                table: "action_log_entries");

            migrationBuilder.AlterColumn<string>(
                name: "permalink",
                table: "versioned_pages",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "reverse_diff",
                table: "page_versions",
                type: "character varying(2098176)",
                maxLength: 2098176,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(2129920)",
                oldMaxLength: 2129920);
        }
    }
}
