using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddNewFileFeatureFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "deleted",
                table: "storage_items",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "important",
                table: "storage_items",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "last_modified_by_id",
                table: "storage_items",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "modification_locked",
                table: "storage_items",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "moved_from_location",
                table: "storage_items",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "deleted",
                table: "storage_item_versions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "uploaded_by_id",
                table: "storage_item_versions",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "storage_item_delete_infos",
                columns: table => new
                {
                    storage_item_id = table.Column<long>(type: "bigint", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    original_folder_id = table.Column<long>(type: "bigint", nullable: true),
                    original_read_access = table.Column<int>(type: "integer", nullable: false),
                    original_write_access = table.Column<int>(type: "integer", nullable: false),
                    original_folder_path = table.Column<string>(type: "text", nullable: false),
                    original_folder_read_access = table.Column<int>(type: "integer", nullable: false),
                    original_folder_write_access = table.Column<int>(type: "integer", nullable: false),
                    original_folder_important = table.Column<bool>(type: "boolean", nullable: false),
                    original_folder_modification_locked = table.Column<bool>(type: "boolean", nullable: false),
                    original_folder_owner_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_storage_item_delete_infos", x => x.storage_item_id);
                    table.ForeignKey(
                        name: "fk_storage_item_delete_infos_storage_items_original_folder_id",
                        column: x => x.original_folder_id,
                        principalTable: "storage_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_storage_item_delete_infos_storage_items_storage_item_id1",
                        column: x => x.storage_item_id,
                        principalTable: "storage_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_storage_item_delete_infos_users_original_folder_owner_id",
                        column: x => x.original_folder_owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_storage_items_last_modified_by_id",
                table: "storage_items",
                column: "last_modified_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_storage_item_versions_uploaded_by_id",
                table: "storage_item_versions",
                column: "uploaded_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_storage_item_delete_infos_original_folder_id",
                table: "storage_item_delete_infos",
                column: "original_folder_id");

            migrationBuilder.CreateIndex(
                name: "ix_storage_item_delete_infos_original_folder_owner_id",
                table: "storage_item_delete_infos",
                column: "original_folder_owner_id");

            migrationBuilder.AddForeignKey(
                name: "fk_storage_item_versions_users_uploaded_by_id",
                table: "storage_item_versions",
                column: "uploaded_by_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_storage_items_users_last_modified_by_id",
                table: "storage_items",
                column: "last_modified_by_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_storage_item_versions_users_uploaded_by_id",
                table: "storage_item_versions");

            migrationBuilder.DropForeignKey(
                name: "fk_storage_items_users_last_modified_by_id",
                table: "storage_items");

            migrationBuilder.DropTable(
                name: "storage_item_delete_infos");

            migrationBuilder.DropIndex(
                name: "ix_storage_items_last_modified_by_id",
                table: "storage_items");

            migrationBuilder.DropIndex(
                name: "ix_storage_item_versions_uploaded_by_id",
                table: "storage_item_versions");

            migrationBuilder.DropColumn(
                name: "deleted",
                table: "storage_items");

            migrationBuilder.DropColumn(
                name: "important",
                table: "storage_items");

            migrationBuilder.DropColumn(
                name: "last_modified_by_id",
                table: "storage_items");

            migrationBuilder.DropColumn(
                name: "modification_locked",
                table: "storage_items");

            migrationBuilder.DropColumn(
                name: "moved_from_location",
                table: "storage_items");

            migrationBuilder.DropColumn(
                name: "deleted",
                table: "storage_item_versions");

            migrationBuilder.DropColumn(
                name: "uploaded_by_id",
                table: "storage_item_versions");
        }
    }
}
