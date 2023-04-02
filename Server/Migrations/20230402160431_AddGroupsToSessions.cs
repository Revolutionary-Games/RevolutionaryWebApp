using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThriveDevCenter.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupsToSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "admin",
                table: "users");

            migrationBuilder.DropColumn(
                name: "developer",
                table: "users");

            migrationBuilder.DropColumn(
                name: "restricted",
                table: "users");

            migrationBuilder.DropColumn(
                name: "session_version",
                table: "users");

            migrationBuilder.DropColumn(
                name: "session_version",
                table: "sessions");

            migrationBuilder.AddColumn<string>(
                name: "cached_user_groups_raw",
                table: "sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cached_user_groups_raw",
                table: "launcher_links",
                type: "text",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "user_groups_extra_data",
                keyColumn: "group_id",
                keyValue: 1,
                columns: new[] { "created_at", "updated_at" },
                values: new object[] { new DateTime(2023, 4, 2, 16, 4, 31, 355, DateTimeKind.Utc).AddTicks(4672), new DateTime(2023, 4, 2, 16, 4, 31, 355, DateTimeKind.Utc).AddTicks(4672) });

            migrationBuilder.UpdateData(
                table: "user_groups_extra_data",
                keyColumn: "group_id",
                keyValue: 3,
                columns: new[] { "created_at", "updated_at" },
                values: new object[] { new DateTime(2023, 4, 2, 16, 4, 31, 355, DateTimeKind.Utc).AddTicks(4672), new DateTime(2023, 4, 2, 16, 4, 31, 355, DateTimeKind.Utc).AddTicks(4672) });

            migrationBuilder.UpdateData(
                table: "user_groups_extra_data",
                keyColumn: "group_id",
                keyValue: 4,
                columns: new[] { "created_at", "updated_at" },
                values: new object[] { new DateTime(2023, 4, 2, 16, 4, 31, 355, DateTimeKind.Utc).AddTicks(4672), new DateTime(2023, 4, 2, 16, 4, 31, 355, DateTimeKind.Utc).AddTicks(4672) });

            migrationBuilder.Sql("-- noinspection SqlWithoutWhere\nDELETE FROM sessions;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cached_user_groups_raw",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "cached_user_groups_raw",
                table: "launcher_links");

            migrationBuilder.AddColumn<bool>(
                name: "admin",
                table: "users",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "developer",
                table: "users",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "restricted",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "session_version",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "session_version",
                table: "sessions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.UpdateData(
                table: "user_groups_extra_data",
                keyColumn: "group_id",
                keyValue: 1,
                columns: new[] { "created_at", "updated_at" },
                values: new object[] { new DateTime(2023, 3, 5, 18, 39, 38, 313, DateTimeKind.Utc).AddTicks(4002), new DateTime(2023, 3, 5, 18, 39, 38, 313, DateTimeKind.Utc).AddTicks(4002) });

            migrationBuilder.UpdateData(
                table: "user_groups_extra_data",
                keyColumn: "group_id",
                keyValue: 3,
                columns: new[] { "created_at", "updated_at" },
                values: new object[] { new DateTime(2023, 3, 5, 18, 39, 38, 313, DateTimeKind.Utc).AddTicks(4002), new DateTime(2023, 3, 5, 18, 39, 38, 313, DateTimeKind.Utc).AddTicks(4002) });

            migrationBuilder.UpdateData(
                table: "user_groups_extra_data",
                keyColumn: "group_id",
                keyValue: 4,
                columns: new[] { "created_at", "updated_at" },
                values: new object[] { new DateTime(2023, 3, 5, 18, 39, 38, 313, DateTimeKind.Utc).AddTicks(4002), new DateTime(2023, 3, 5, 18, 39, 38, 313, DateTimeKind.Utc).AddTicks(4002) });
        }
    }
}
