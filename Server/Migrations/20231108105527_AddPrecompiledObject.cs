using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddPrecompiledObject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "precompiled_objects",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: false),
                    total_storage_size = table.Column<long>(type: "bigint", nullable: false),
                    @public = table.Column<bool>(name: "public", type: "boolean", nullable: false),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_precompiled_objects", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "precompiled_object_versions",
                columns: table => new
                {
                    owned_by_id = table.Column<long>(type: "bigint", nullable: false),
                    version = table.Column<string>(type: "text", nullable: false),
                    platform = table.Column<int>(type: "integer", nullable: false),
                    tags = table.Column<int>(type: "integer", nullable: false),
                    uploaded = table.Column<bool>(type: "boolean", nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_download = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    stored_in_item_id = table.Column<long>(type: "bigint", nullable: false),
                    created_by_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_precompiled_object_versions", x => new { x.owned_by_id, x.version, x.platform, x.tags });
                    table.ForeignKey(
                        name: "fk_precompiled_object_versions_precompiled_objects_owned_by_id",
                        column: x => x.owned_by_id,
                        principalTable: "precompiled_objects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_precompiled_object_versions_storage_items_stored_in_item_id",
                        column: x => x.stored_in_item_id,
                        principalTable: "storage_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_precompiled_object_versions_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_precompiled_object_versions_created_by_id",
                table: "precompiled_object_versions",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_precompiled_object_versions_stored_in_item_id",
                table: "precompiled_object_versions",
                column: "stored_in_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_precompiled_objects_name",
                table: "precompiled_objects",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "precompiled_object_versions");

            migrationBuilder.DropTable(
                name: "precompiled_objects");
        }
    }
}
