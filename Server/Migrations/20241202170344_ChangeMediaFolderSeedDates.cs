using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    /// <inheritdoc />
    public partial class ChangeMediaFolderSeedDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "media_folders",
                keyColumn: "id",
                keyValue: 1L,
                columns: new[] { "created_at", "updated_at" },
                values: new object[] { new DateTime(2024, 8, 4, 19, 0, 0, 0, DateTimeKind.Utc), new DateTime(2024, 8, 4, 19, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "media_folders",
                keyColumn: "id",
                keyValue: 2L,
                columns: new[] { "created_at", "updated_at" },
                values: new object[] { new DateTime(2024, 8, 4, 19, 0, 0, 0, DateTimeKind.Utc), new DateTime(2024, 8, 4, 19, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "media_folders",
                keyColumn: "id",
                keyValue: 3L,
                columns: new[] { "created_at", "updated_at" },
                values: new object[] { new DateTime(2024, 8, 4, 19, 0, 0, 0, DateTimeKind.Utc), new DateTime(2024, 8, 4, 19, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "media_folders",
                keyColumn: "id",
                keyValue: 4L,
                columns: new[] { "created_at", "updated_at" },
                values: new object[] { new DateTime(2024, 8, 4, 19, 0, 0, 0, DateTimeKind.Utc), new DateTime(2024, 8, 4, 19, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "media_folders",
                keyColumn: "id",
                keyValue: 9L,
                columns: new[] { "created_at", "updated_at" },
                values: new object[] { new DateTime(2024, 8, 4, 19, 0, 0, 0, DateTimeKind.Utc), new DateTime(2024, 8, 4, 19, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "media_folders",
                keyColumn: "id",
                keyValue: 10L,
                columns: new[] { "created_at", "updated_at" },
                values: new object[] { new DateTime(2024, 8, 4, 19, 0, 0, 0, DateTimeKind.Utc), new DateTime(2024, 8, 4, 19, 0, 0, 0, DateTimeKind.Utc) });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "media_folders",
                keyColumn: "id",
                keyValue: 1L,
                columns: new[] { "created_at", "updated_at" },
                values: new object[] { new DateTime(2014, 8, 4, 19, 0, 0, 0, DateTimeKind.Utc), new DateTime(2014, 8, 4, 19, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "media_folders",
                keyColumn: "id",
                keyValue: 2L,
                columns: new[] { "created_at", "updated_at" },
                values: new object[] { new DateTime(2014, 8, 4, 19, 0, 0, 0, DateTimeKind.Utc), new DateTime(2014, 8, 4, 19, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "media_folders",
                keyColumn: "id",
                keyValue: 3L,
                columns: new[] { "created_at", "updated_at" },
                values: new object[] { new DateTime(2014, 8, 4, 19, 0, 0, 0, DateTimeKind.Utc), new DateTime(2014, 8, 4, 19, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "media_folders",
                keyColumn: "id",
                keyValue: 4L,
                columns: new[] { "created_at", "updated_at" },
                values: new object[] { new DateTime(2014, 8, 4, 19, 0, 0, 0, DateTimeKind.Utc), new DateTime(2014, 8, 4, 19, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "media_folders",
                keyColumn: "id",
                keyValue: 9L,
                columns: new[] { "created_at", "updated_at" },
                values: new object[] { new DateTime(2014, 8, 4, 19, 0, 0, 0, DateTimeKind.Utc), new DateTime(2014, 8, 4, 19, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "media_folders",
                keyColumn: "id",
                keyValue: 10L,
                columns: new[] { "created_at", "updated_at" },
                values: new object[] { new DateTime(2014, 8, 4, 19, 0, 0, 0, DateTimeKind.Utc), new DateTime(2014, 8, 4, 19, 0, 0, 0, DateTimeKind.Utc) });
        }
    }
}
