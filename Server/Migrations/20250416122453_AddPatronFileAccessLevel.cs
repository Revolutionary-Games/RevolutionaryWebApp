using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddPatronFileAccessLevel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "suspended",
                table: "users");

            migrationBuilder.AddColumn<DateTime>(
                name: "suspended_until",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.InsertData(
                table: "user_groups",
                columns: new[] { "id", "name" },
                values: new object[] { 14, "PatreonSupporter" });

            migrationBuilder.InsertData(
                table: "user_groups_extra_data",
                columns: new[] { "group_id", "created_at", "custom_description", "updated_at" },
                values: new object[] { 14, new DateTime(2023, 4, 2, 16, 4, 31, 355, DateTimeKind.Utc).AddTicks(4672), "Inbuilt group, cannot be modified", new DateTime(2023, 4, 2, 16, 4, 31, 355, DateTimeKind.Utc).AddTicks(4672) });

            // Special value migration for FileAccess
            migrationBuilder.Sql("UPDATE storage_items SET read_access = read_access + 1 WHERE read_access > 1;");
            migrationBuilder.Sql("UPDATE storage_items SET write_access = write_access + 1 WHERE write_access > 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "user_groups_extra_data",
                keyColumn: "group_id",
                keyValue: 14);

            migrationBuilder.DeleteData(
                table: "user_groups",
                keyColumn: "id",
                keyValue: 14);

            migrationBuilder.DropColumn(
                name: "suspended_until",
                table: "users");

            migrationBuilder.AddColumn<bool>(
                name: "suspended",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("UPDATE storage_items SET read_access = read_access - 1 WHERE read_access > 2;");
            migrationBuilder.Sql("UPDATE storage_items SET write_access = write_access - 1 WHERE write_access > 2;");
        }
    }
}
