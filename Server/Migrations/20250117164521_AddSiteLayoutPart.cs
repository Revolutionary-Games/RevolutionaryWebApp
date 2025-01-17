using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteLayoutPart : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "dump_local_file_name",
                table: "crash_reports",
                newName: "upload_storage_path");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_media_files_global_id",
                table: "media_files",
                column: "global_id");

            migrationBuilder.CreateTable(
                name: "site_layout_part",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    link_target = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    alt_text = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    part_type = table.Column<int>(type: "integer", nullable: false),
                    image_id = table.Column<Guid>(type: "uuid", nullable: true),
                    order = table.Column<int>(type: "integer", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_site_layout_part", x => x.id);
                    table.ForeignKey(
                        name: "fk_site_layout_part_media_files_image_id",
                        column: x => x.image_id,
                        principalTable: "media_files",
                        principalColumn: "global_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_site_layout_part_image_id",
                table: "site_layout_part",
                column: "image_id");

            migrationBuilder.CreateIndex(
                name: "ix_site_layout_part_part_type_order",
                table: "site_layout_part",
                columns: new[] { "part_type", "order" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "site_layout_part");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_media_files_global_id",
                table: "media_files");

            migrationBuilder.RenameColumn(
                name: "upload_storage_path",
                table: "crash_reports",
                newName: "dump_local_file_name");
        }
    }
}
