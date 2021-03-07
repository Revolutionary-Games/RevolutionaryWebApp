using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace ThriveDevCenter.Server.Migrations
{
    public partial class InitialCreate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "EntityFrameworkHiLoSequence",
                incrementBy: 10);

            migrationBuilder.CreateTable(
                name: "access_keys",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    description = table.Column<string>(type: "text", nullable: false),
                    last_used = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    key_code = table.Column<string>(type: "text", nullable: false),
                    key_type = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_access_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lfs_projects",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: true),
                    slug = table.Column<string>(type: "text", nullable: true),
                    @public = table.Column<bool>(name: "public", type: "boolean", nullable: false),
                    repo_url = table.Column<string>(type: "text", nullable: false),
                    clone_url = table.Column<string>(type: "text", nullable: true),
                    total_object_size = table.Column<int>(type: "integer", nullable: true),
                    total_object_count = table.Column<int>(type: "integer", nullable: true),
                    total_size_updated = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    file_tree_updated = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    file_tree_commit = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lfs_projects", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "patreon_settings",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    creator_token = table.Column<string>(type: "text", nullable: false),
                    creator_refresh_token = table.Column<string>(type: "text", nullable: true),
                    webhook_id = table.Column<string>(type: "text", nullable: false),
                    webhook_secret = table.Column<string>(type: "text", nullable: false),
                    last_webhook = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    last_refreshed = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    campaign_id = table.Column<string>(type: "text", nullable: true),
                    devbuilds_reward_id = table.Column<string>(type: "text", nullable: true),
                    vip_reward_id = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_patreon_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "patrons",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    email = table.Column<string>(type: "text", nullable: false),
                    email_alias = table.Column<string>(type: "text", nullable: true),
                    username = table.Column<string>(type: "text", nullable: false),
                    pledge_amount_cents = table.Column<int>(type: "integer", nullable: false),
                    reward_id = table.Column<string>(type: "text", nullable: false),
                    marked = table.Column<bool>(type: "boolean", nullable: true),
                    patreon_token = table.Column<string>(type: "text", nullable: true),
                    patreon_refresh_token = table.Column<string>(type: "text", nullable: true),
                    has_forum_account = table.Column<bool>(type: "boolean", nullable: true),
                    suspended = table.Column<bool>(type: "boolean", nullable: true),
                    suspended_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_patrons", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "storage_files",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SequenceHiLo),
                    storage_path = table.Column<string>(type: "text", nullable: false),
                    size = table.Column<int>(type: "integer", nullable: true),
                    allow_parentless = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    uploading = table.Column<bool>(type: "boolean", nullable: false),
                    upload_expires = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_storage_files", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    email = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: true),
                    local = table.Column<bool>(type: "boolean", nullable: false),
                    sso_source = table.Column<string>(type: "text", nullable: true),
                    password_digest = table.Column<string>(type: "text", nullable: true),
                    developer = table.Column<bool>(type: "boolean", nullable: true),
                    admin = table.Column<bool>(type: "boolean", nullable: true),
                    api_token = table.Column<string>(type: "text", nullable: true),
                    lfs_token = table.Column<string>(type: "text", nullable: true),
                    suspended = table.Column<bool>(type: "boolean", nullable: true, defaultValue: false),
                    suspended_reason = table.Column<string>(type: "text", nullable: true),
                    suspended_manually = table.Column<bool>(type: "boolean", nullable: true, defaultValue: false),
                    launcher_link_code = table.Column<string>(type: "text", nullable: true),
                    launcher_code_expires = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    total_launcher_links = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    session_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lfs_objects",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SequenceHiLo),
                    lfs_oid = table.Column<string>(type: "text", nullable: false),
                    size = table.Column<int>(type: "integer", nullable: false),
                    storage_path = table.Column<string>(type: "text", nullable: false),
                    lfs_project_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lfs_objects", x => x.id);
                    table.ForeignKey(
                        name: "fk_lfs_objects_lfs_projects_lfs_project_id",
                        column: x => x.lfs_project_id,
                        principalTable: "lfs_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_git_files",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SequenceHiLo),
                    name = table.Column<string>(type: "text", nullable: true),
                    path = table.Column<string>(type: "text", nullable: true),
                    size = table.Column<int>(type: "integer", nullable: true),
                    ftype = table.Column<string>(type: "text", nullable: true),
                    lfs_oid = table.Column<string>(type: "text", nullable: true),
                    lfs_project_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_git_files", x => x.id);
                    table.ForeignKey(
                        name: "fk_project_git_files_lfs_projects_lfs_project_id",
                        column: x => x.lfs_project_id,
                        principalTable: "lfs_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "launcher_links",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    link_code = table.Column<string>(type: "text", nullable: false),
                    last_ip = table.Column<string>(type: "text", nullable: false),
                    last_connection = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    total_api_calls = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_launcher_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_launcher_links_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "storage_items",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SequenceHiLo),
                    name = table.Column<string>(type: "text", nullable: true),
                    ftype = table.Column<int>(type: "integer", nullable: true),
                    special = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    size = table.Column<int>(type: "integer", nullable: true),
                    read_access = table.Column<int>(type: "integer", nullable: true, defaultValue: 2),
                    write_access = table.Column<int>(type: "integer", nullable: true, defaultValue: 2),
                    owner_id = table.Column<long>(type: "bigint", nullable: true),
                    parent_id = table.Column<long>(type: "bigint", nullable: true),
                    allow_parentless = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_storage_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_storage_items_storage_items_parent_id",
                        column: x => x.parent_id,
                        principalTable: "storage_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_storage_items_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "dehydrated_objects",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SequenceHiLo),
                    sha3 = table.Column<string>(type: "text", nullable: false),
                    storage_item_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dehydrated_objects", x => x.id);
                    table.ForeignKey(
                        name: "fk_dehydrated_objects_storage_items_storage_item_id",
                        column: x => x.storage_item_id,
                        principalTable: "storage_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dev_builds",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    build_hash = table.Column<string>(type: "text", nullable: false),
                    platform = table.Column<string>(type: "text", nullable: false),
                    branch = table.Column<string>(type: "text", nullable: false),
                    build_zip_hash = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    score = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    downloads = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    important = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    keep = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    pr_url = table.Column<string>(type: "text", nullable: true),
                    pr_fetched = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    build_of_the_day = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    anonymous = table.Column<bool>(type: "boolean", nullable: false),
                    verified = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    verified_by_id = table.Column<long>(type: "bigint", nullable: true),
                    storage_item_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dev_builds", x => x.id);
                    table.ForeignKey(
                        name: "fk_dev_builds_storage_items_storage_item_id",
                        column: x => x.storage_item_id,
                        principalTable: "storage_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dev_builds_users_verified_by_id",
                        column: x => x.verified_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "storage_item_versions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SequenceHiLo),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    storage_item_id = table.Column<long>(type: "bigint", nullable: false),
                    storage_file_id = table.Column<long>(type: "bigint", nullable: false),
                    keep = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    @protected = table.Column<bool>(name: "protected", type: "boolean", nullable: false, defaultValue: false),
                    uploading = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_storage_item_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_storage_item_versions_storage_files_storage_file_id",
                        column: x => x.storage_file_id,
                        principalTable: "storage_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_storage_item_versions_storage_items_storage_item_id",
                        column: x => x.storage_item_id,
                        principalTable: "storage_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dehydrated_objects_dev_builds",
                columns: table => new
                {
                    dehydrated_objects_id = table.Column<long>(type: "bigint", nullable: false),
                    dev_builds_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dehydrated_objects_dev_builds", x => new { x.dehydrated_objects_id, x.dev_builds_id });
                    table.ForeignKey(
                        name: "fk_dehydrated_objects_dev_builds_dehydrated_objects_dehydrated",
                        column: x => x.dehydrated_objects_id,
                        principalTable: "dehydrated_objects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_dehydrated_objects_dev_builds_dev_builds_dev_builds_id",
                        column: x => x.dev_builds_id,
                        principalTable: "dev_builds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_access_keys_key_code",
                table: "access_keys",
                column: "key_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dehydrated_objects_sha3",
                table: "dehydrated_objects",
                column: "sha3",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dehydrated_objects_storage_item_id",
                table: "dehydrated_objects",
                column: "storage_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_dehydrated_objects_dev_builds_dev_builds_id",
                table: "dehydrated_objects_dev_builds",
                column: "dev_builds_id");

            migrationBuilder.CreateIndex(
                name: "ix_dev_builds_anonymous",
                table: "dev_builds",
                column: "anonymous");

            migrationBuilder.CreateIndex(
                name: "ix_dev_builds_build_hash_platform",
                table: "dev_builds",
                columns: new[] { "build_hash", "platform" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dev_builds_storage_item_id",
                table: "dev_builds",
                column: "storage_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_dev_builds_verified_by_id",
                table: "dev_builds",
                column: "verified_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_launcher_links_link_code",
                table: "launcher_links",
                column: "link_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_launcher_links_user_id",
                table: "launcher_links",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_lfs_objects_lfs_project_id_lfs_oid",
                table: "lfs_objects",
                columns: new[] { "lfs_project_id", "lfs_oid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_lfs_projects_name",
                table: "lfs_projects",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_lfs_projects_slug",
                table: "lfs_projects",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_patreon_settings_webhook_id",
                table: "patreon_settings",
                column: "webhook_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_patrons_email",
                table: "patrons",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_patrons_email_alias",
                table: "patrons",
                column: "email_alias",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_project_git_files_lfs_project_id",
                table: "project_git_files",
                column: "lfs_project_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_git_files_path_name_lfs_project_id",
                table: "project_git_files",
                columns: new[] { "path", "name", "lfs_project_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_storage_files_storage_path",
                table: "storage_files",
                column: "storage_path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_storage_files_uploading",
                table: "storage_files",
                column: "uploading");

            migrationBuilder.CreateIndex(
                name: "ix_storage_item_versions_storage_file_id",
                table: "storage_item_versions",
                column: "storage_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_storage_item_versions_storage_item_id_version",
                table: "storage_item_versions",
                columns: new[] { "storage_item_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_storage_items_allow_parentless",
                table: "storage_items",
                column: "allow_parentless");

            migrationBuilder.CreateIndex(
                name: "ix_storage_items_name",
                table: "storage_items",
                column: "name",
                unique: true,
                filter: "(parent_id IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_storage_items_name_parent_id",
                table: "storage_items",
                columns: new[] { "name", "parent_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_storage_items_owner_id",
                table: "storage_items",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_storage_items_parent_id",
                table: "storage_items",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_api_token",
                table: "users",
                column: "api_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_launcher_link_code",
                table: "users",
                column: "launcher_link_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_lfs_token",
                table: "users",
                column: "lfs_token",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "access_keys");

            migrationBuilder.DropTable(
                name: "dehydrated_objects_dev_builds");

            migrationBuilder.DropTable(
                name: "launcher_links");

            migrationBuilder.DropTable(
                name: "lfs_objects");

            migrationBuilder.DropTable(
                name: "patreon_settings");

            migrationBuilder.DropTable(
                name: "patrons");

            migrationBuilder.DropTable(
                name: "project_git_files");

            migrationBuilder.DropTable(
                name: "storage_item_versions");

            migrationBuilder.DropTable(
                name: "dehydrated_objects");

            migrationBuilder.DropTable(
                name: "dev_builds");

            migrationBuilder.DropTable(
                name: "lfs_projects");

            migrationBuilder.DropTable(
                name: "storage_files");

            migrationBuilder.DropTable(
                name: "storage_items");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropSequence(
                name: "EntityFrameworkHiLoSequence");
        }
    }
}
