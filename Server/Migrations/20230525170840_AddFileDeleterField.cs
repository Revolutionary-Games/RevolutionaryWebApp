using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddFileDeleterField : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "deleted_by_id",
                table: "storage_item_delete_infos",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_storage_item_delete_infos_deleted_by_id",
                table: "storage_item_delete_infos",
                column: "deleted_by_id");

            migrationBuilder.AddForeignKey(
                name: "fk_storage_item_delete_infos_users_deleted_by_id",
                table: "storage_item_delete_infos",
                column: "deleted_by_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_storage_item_delete_infos_users_deleted_by_id",
                table: "storage_item_delete_infos");

            migrationBuilder.DropIndex(
                name: "ix_storage_item_delete_infos_deleted_by_id",
                table: "storage_item_delete_infos");

            migrationBuilder.DropColumn(
                name: "deleted_by_id",
                table: "storage_item_delete_infos");
        }
    }
}
