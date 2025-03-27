using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    /// <inheritdoc />
    public partial class SwitchMediaUsageToGUID : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_media_file_usages_media_files_media_file_id",
                table: "media_file_usages");

            migrationBuilder.DropPrimaryKey(
                name: "pk_media_file_usages",
                table: "media_file_usages");

            migrationBuilder.DropColumn(
                name: "media_file_id",
                table: "media_file_usages");

            migrationBuilder.AddColumn<Guid>(
                name: "media_file_guid",
                table: "media_file_usages",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddPrimaryKey(
                name: "pk_media_file_usages",
                table: "media_file_usages",
                columns: new[] { "used_by_resource", "usage", "media_file_guid" });

            migrationBuilder.CreateIndex(
                name: "ix_media_file_usages_media_file_guid",
                table: "media_file_usages",
                column: "media_file_guid");

            migrationBuilder.AddForeignKey(
                name: "fk_media_file_usages_media_files_media_file_guid",
                table: "media_file_usages",
                column: "media_file_guid",
                principalTable: "media_files",
                principalColumn: "global_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_media_file_usages_media_files_media_file_guid",
                table: "media_file_usages");

            migrationBuilder.DropPrimaryKey(
                name: "pk_media_file_usages",
                table: "media_file_usages");

            migrationBuilder.DropIndex(
                name: "ix_media_file_usages_media_file_guid",
                table: "media_file_usages");

            migrationBuilder.DropColumn(
                name: "media_file_guid",
                table: "media_file_usages");

            migrationBuilder.AddColumn<long>(
                name: "media_file_id",
                table: "media_file_usages",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddPrimaryKey(
                name: "pk_media_file_usages",
                table: "media_file_usages",
                columns: new[] { "media_file_id", "usage", "used_by_resource" });

            migrationBuilder.AddForeignKey(
                name: "fk_media_file_usages_media_files_media_file_id",
                table: "media_file_usages",
                column: "media_file_id",
                principalTable: "media_files",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
