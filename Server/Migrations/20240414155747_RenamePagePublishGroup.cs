using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    /// <inheritdoc />
    public partial class RenamePagePublishGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "user_groups",
                keyColumn: "id",
                keyValue: 10,
                column: "name",
                value: "SitePagePublisher");

            migrationBuilder.InsertData(
                table: "user_groups",
                columns: new[] { "id", "name" },
                values: new object[] { 11, "WikiEditor" });

            migrationBuilder.InsertData(
                table: "user_groups_extra_data",
                columns: new[] { "group_id", "created_at", "custom_description", "updated_at" },
                values: new object[] { 11, new DateTime(2023, 4, 2, 16, 4, 31, 355, DateTimeKind.Utc).AddTicks(4672), "Inbuilt group, cannot be modified", new DateTime(2023, 4, 2, 16, 4, 31, 355, DateTimeKind.Utc).AddTicks(4672) });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "user_groups_extra_data",
                keyColumn: "group_id",
                keyValue: 11);

            migrationBuilder.DeleteData(
                table: "user_groups",
                keyColumn: "id",
                keyValue: 11);

            migrationBuilder.UpdateData(
                table: "user_groups",
                keyColumn: "id",
                keyValue: 10,
                column: "name",
                value: "SiteLayoutPublisher");
        }
    }
}
