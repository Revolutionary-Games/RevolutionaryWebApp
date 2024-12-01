using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace RevolutionaryWebApp.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "created_at",
                table: "page_versions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateTable(
                name: "media_folders",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    parent_folder_id = table.Column<long>(type: "bigint", nullable: true),
                    content_write_access = table.Column<int>(type: "integer", nullable: false),
                    content_read_access = table.Column<int>(type: "integer", nullable: false),
                    sub_folder_modify_access = table.Column<int>(type: "integer", nullable: false),
                    folder_modify_access = table.Column<int>(type: "integer", nullable: false),
                    owned_by_id = table.Column<long>(type: "bigint", nullable: true),
                    last_modified_by_id = table.Column<long>(type: "bigint", nullable: true),
                    delete_if_empty = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_media_folders", x => x.id);
                    table.ForeignKey(
                        name: "fk_media_folders_media_folders_parent_folder_id",
                        column: x => x.parent_folder_id,
                        principalTable: "media_folders",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_media_folders_users_last_modified_by_id",
                        column: x => x.last_modified_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_media_folders_users_owned_by_id",
                        column: x => x.owned_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "media_files",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    global_id = table.Column<Guid>(type: "uuid", nullable: false),
                    folder_id = table.Column<long>(type: "bigint", nullable: false),
                    original_file_size = table.Column<long>(type: "bigint", nullable: false),
                    metadata_visibility = table.Column<int>(type: "integer", nullable: false),
                    modify_access = table.Column<int>(type: "integer", nullable: false),
                    uploaded_by_id = table.Column<long>(type: "bigint", nullable: true),
                    last_modified_by_id = table.Column<long>(type: "bigint", nullable: true),
                    processed = table.Column<bool>(type: "boolean", nullable: false),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_media_files", x => x.id);
                    table.ForeignKey(
                        name: "fk_media_files_media_folders_folder_id",
                        column: x => x.folder_id,
                        principalTable: "media_folders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_media_files_users_last_modified_by_id",
                        column: x => x.last_modified_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_media_files_users_uploaded_by_id",
                        column: x => x.uploaded_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.InsertData(
                table: "media_folders",
                columns: new[] { "id", "content_read_access", "content_write_access", "created_at", "delete_if_empty", "folder_modify_access", "last_modified_by_id", "name", "owned_by_id", "parent_folder_id", "sub_folder_modify_access", "updated_at" },
                values: new object[,]
                {
                    { 1L, 3, 7, new DateTime(2014, 8, 4, 19, 0, 0, 0, DateTimeKind.Utc), false, 5, null, "Website Parts", null, null, 4, new DateTime(2014, 8, 4, 19, 0, 0, 0, DateTimeKind.Utc) },
                    { 2L, 3, 7, new DateTime(2014, 8, 4, 19, 0, 0, 0, DateTimeKind.Utc), false, 5, null, "Website Pages", null, null, 4, new DateTime(2014, 8, 4, 19, 0, 0, 0, DateTimeKind.Utc) },
                    { 3L, 3, 7, new DateTime(2014, 8, 4, 19, 0, 0, 0, DateTimeKind.Utc), false, 5, null, "Website Posts", null, null, 4, new DateTime(2014, 8, 4, 19, 0, 0, 0, DateTimeKind.Utc) },
                    { 4L, 3, 7, new DateTime(2014, 8, 4, 19, 0, 0, 0, DateTimeKind.Utc), false, 5, null, "Wiki Media", null, null, 4, new DateTime(2014, 8, 4, 19, 0, 0, 0, DateTimeKind.Utc) },
                    { 9L, 1, 2, new DateTime(2014, 8, 4, 19, 0, 0, 0, DateTimeKind.Utc), false, 5, null, "User Avatars", null, null, 5, new DateTime(2014, 8, 4, 19, 0, 0, 0, DateTimeKind.Utc) },
                    { 10L, 1, 2, new DateTime(2014, 8, 4, 19, 0, 0, 0, DateTimeKind.Utc), false, 5, null, "User Uploads", null, null, 5, new DateTime(2014, 8, 4, 19, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.CreateIndex(
                name: "ix_media_files_folder_id",
                table: "media_files",
                column: "folder_id");

            migrationBuilder.CreateIndex(
                name: "ix_media_files_global_id",
                table: "media_files",
                column: "global_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_media_files_last_modified_by_id",
                table: "media_files",
                column: "last_modified_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_media_files_name_folder_id",
                table: "media_files",
                columns: new[] { "name", "folder_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_media_files_uploaded_by_id",
                table: "media_files",
                column: "uploaded_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_media_folders_last_modified_by_id",
                table: "media_folders",
                column: "last_modified_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_media_folders_name_parent_folder_id",
                table: "media_folders",
                columns: new[] { "name", "parent_folder_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_media_folders_owned_by_id",
                table: "media_folders",
                column: "owned_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_media_folders_parent_folder_id",
                table: "media_folders",
                column: "parent_folder_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "media_files");

            migrationBuilder.DropTable(
                name: "media_folders");

            migrationBuilder.DropColumn(
                name: "created_at",
                table: "page_versions");
        }
    }
}
