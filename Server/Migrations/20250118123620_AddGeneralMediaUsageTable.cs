using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddGeneralMediaUsageTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_site_layout_part_media_files_image_id",
                table: "site_layout_part");

            migrationBuilder.DropPrimaryKey(
                name: "pk_site_layout_part",
                table: "site_layout_part");

            migrationBuilder.RenameTable(
                name: "site_layout_part",
                newName: "site_layout_parts");

            migrationBuilder.RenameIndex(
                name: "ix_site_layout_part_part_type_order",
                table: "site_layout_parts",
                newName: "ix_site_layout_parts_part_type_order");

            migrationBuilder.RenameIndex(
                name: "ix_site_layout_part_image_id",
                table: "site_layout_parts",
                newName: "ix_site_layout_parts_image_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_site_layout_parts",
                table: "site_layout_parts",
                column: "id");

            migrationBuilder.CreateTable(
                name: "media_file_usages",
                columns: table => new
                {
                    media_file_id = table.Column<long>(type: "bigint", nullable: false),
                    used_by_resource = table.Column<long>(type: "bigint", nullable: false),
                    usage = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_media_file_usages", x => new { x.media_file_id, x.usage, x.used_by_resource });
                    table.ForeignKey(
                        name: "fk_media_file_usages_media_files_media_file_id",
                        column: x => x.media_file_id,
                        principalTable: "media_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddForeignKey(
                name: "fk_site_layout_parts_media_files_image_id",
                table: "site_layout_parts",
                column: "image_id",
                principalTable: "media_files",
                principalColumn: "global_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_site_layout_parts_media_files_image_id",
                table: "site_layout_parts");

            migrationBuilder.DropTable(
                name: "media_file_usages");

            migrationBuilder.DropPrimaryKey(
                name: "pk_site_layout_parts",
                table: "site_layout_parts");

            migrationBuilder.RenameTable(
                name: "site_layout_parts",
                newName: "site_layout_part");

            migrationBuilder.RenameIndex(
                name: "ix_site_layout_parts_part_type_order",
                table: "site_layout_part",
                newName: "ix_site_layout_part_part_type_order");

            migrationBuilder.RenameIndex(
                name: "ix_site_layout_parts_image_id",
                table: "site_layout_part",
                newName: "ix_site_layout_part_image_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_site_layout_part",
                table: "site_layout_part",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_site_layout_part_media_files_image_id",
                table: "site_layout_part",
                column: "image_id",
                principalTable: "media_files",
                principalColumn: "global_id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
