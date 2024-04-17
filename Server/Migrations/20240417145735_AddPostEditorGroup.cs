using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddPostEditorGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "user_groups",
                columns: new[] { "id", "name" },
                values: new object[] { 12, "PostEditor" });

            migrationBuilder.InsertData(
                table: "user_groups_extra_data",
                columns: new[] { "group_id", "created_at", "custom_description", "updated_at" },
                values: new object[] { 12, new DateTime(2023, 4, 2, 16, 4, 31, 355, DateTimeKind.Utc).AddTicks(4672), "Inbuilt group, cannot be modified", new DateTime(2023, 4, 2, 16, 4, 31, 355, DateTimeKind.Utc).AddTicks(4672) });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "user_groups_extra_data",
                keyColumn: "group_id",
                keyValue: 12);

            migrationBuilder.DeleteData(
                table: "user_groups",
                keyColumn: "id",
                keyValue: 12);
        }
    }
}
