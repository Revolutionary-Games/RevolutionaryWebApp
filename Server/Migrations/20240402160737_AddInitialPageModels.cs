using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace RevolutionaryWebApp.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddInitialPageModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "versioned_pages",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    latest_content = table.Column<string>(type: "character varying(2097152)", maxLength: 2097152, nullable: false),
                    visibility = table.Column<int>(type: "integer", nullable: false),
                    permalink = table.Column<string>(type: "text", nullable: true),
                    published_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_edit_comment = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    creator_id = table.Column<long>(type: "bigint", nullable: true),
                    last_editor_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_versioned_pages", x => x.id);
                    table.ForeignKey(
                        name: "fk_versioned_pages_users_creator_id",
                        column: x => x.creator_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_versioned_pages_users_last_editor_id",
                        column: x => x.last_editor_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "page_edit_suggestions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    page_id = table.Column<long>(type: "bigint", nullable: false),
                    score = table.Column<int>(type: "integer", nullable: false),
                    applies_to_page_version = table.Column<int>(type: "integer", nullable: false),
                    edit_comment = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    suggested_changes_diff = table.Column<string>(type: "character varying(2098176)", maxLength: 2098176, nullable: false),
                    suggested_by_id = table.Column<long>(type: "bigint", nullable: false),
                    voted_up_by = table.Column<string>(type: "character varying(1048576)", maxLength: 1048576, nullable: true),
                    voted_down_by = table.Column<string>(type: "character varying(1048576)", maxLength: 1048576, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_page_edit_suggestions", x => x.id);
                    table.ForeignKey(
                        name: "fk_page_edit_suggestions_users_suggested_by_id",
                        column: x => x.suggested_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_page_edit_suggestions_versioned_pages_page_id",
                        column: x => x.page_id,
                        principalTable: "versioned_pages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "page_versions",
                columns: table => new
                {
                    page_id = table.Column<long>(type: "bigint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    edit_comment = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    reverse_diff = table.Column<string>(type: "character varying(2098176)", maxLength: 2098176, nullable: false),
                    edited_by_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_page_versions", x => new { x.page_id, x.version });
                    table.ForeignKey(
                        name: "fk_page_versions_users_edited_by_id",
                        column: x => x.edited_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_page_versions_versioned_pages_page_id",
                        column: x => x.page_id,
                        principalTable: "versioned_pages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "user_groups",
                columns: new[] { "id", "name" },
                values: new object[,]
                {
                    { 6, "SitePageEditor" },
                    { 7, "SiteLayoutEditor" },
                    { 8, "PostPublisher" },
                    { 9, "TemplateEditor" }
                });

            migrationBuilder.InsertData(
                table: "user_groups_extra_data",
                columns: new[] { "group_id", "created_at", "custom_description", "updated_at" },
                values: new object[,]
                {
                    { 6, new DateTime(2023, 4, 2, 16, 4, 31, 355, DateTimeKind.Utc).AddTicks(4672), "Inbuilt group, cannot be modified", new DateTime(2023, 4, 2, 16, 4, 31, 355, DateTimeKind.Utc).AddTicks(4672) },
                    { 7, new DateTime(2023, 4, 2, 16, 4, 31, 355, DateTimeKind.Utc).AddTicks(4672), "Inbuilt group, cannot be modified", new DateTime(2023, 4, 2, 16, 4, 31, 355, DateTimeKind.Utc).AddTicks(4672) },
                    { 8, new DateTime(2023, 4, 2, 16, 4, 31, 355, DateTimeKind.Utc).AddTicks(4672), "Inbuilt group, cannot be modified", new DateTime(2023, 4, 2, 16, 4, 31, 355, DateTimeKind.Utc).AddTicks(4672) },
                    { 9, new DateTime(2023, 4, 2, 16, 4, 31, 355, DateTimeKind.Utc).AddTicks(4672), "Inbuilt group, cannot be modified", new DateTime(2023, 4, 2, 16, 4, 31, 355, DateTimeKind.Utc).AddTicks(4672) }
                });

            migrationBuilder.CreateIndex(
                name: "ix_page_edit_suggestions_page_id_suggested_by_id",
                table: "page_edit_suggestions",
                columns: new[] { "page_id", "suggested_by_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_page_edit_suggestions_suggested_by_id",
                table: "page_edit_suggestions",
                column: "suggested_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_page_versions_edited_by_id",
                table: "page_versions",
                column: "edited_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_page_versions_page_id_version",
                table: "page_versions",
                columns: new[] { "page_id", "version" },
                unique: true,
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_versioned_pages_creator_id",
                table: "versioned_pages",
                column: "creator_id");

            migrationBuilder.CreateIndex(
                name: "ix_versioned_pages_last_editor_id",
                table: "versioned_pages",
                column: "last_editor_id");

            migrationBuilder.CreateIndex(
                name: "ix_versioned_pages_permalink",
                table: "versioned_pages",
                column: "permalink",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_versioned_pages_title",
                table: "versioned_pages",
                column: "title",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "page_edit_suggestions");

            migrationBuilder.DropTable(
                name: "page_versions");

            migrationBuilder.DropTable(
                name: "versioned_pages");

            migrationBuilder.DeleteData(
                table: "user_groups_extra_data",
                keyColumn: "group_id",
                keyValue: 6);

            migrationBuilder.DeleteData(
                table: "user_groups_extra_data",
                keyColumn: "group_id",
                keyValue: 7);

            migrationBuilder.DeleteData(
                table: "user_groups_extra_data",
                keyColumn: "group_id",
                keyValue: 8);

            migrationBuilder.DeleteData(
                table: "user_groups_extra_data",
                keyColumn: "group_id",
                keyValue: 9);

            migrationBuilder.DeleteData(
                table: "user_groups",
                keyColumn: "id",
                keyValue: 6);

            migrationBuilder.DeleteData(
                table: "user_groups",
                keyColumn: "id",
                keyValue: 7);

            migrationBuilder.DeleteData(
                table: "user_groups",
                keyColumn: "id",
                keyValue: 8);

            migrationBuilder.DeleteData(
                table: "user_groups",
                keyColumn: "id",
                keyValue: 9);
        }
    }
}
