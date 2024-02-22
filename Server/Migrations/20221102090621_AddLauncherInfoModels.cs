using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    public partial class AddLauncherInfoModels : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "launcher_download_mirrors",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    internal_name = table.Column<string>(type: "text", nullable: false),
                    info_link = table.Column<string>(type: "text", nullable: false),
                    readable_name = table.Column<string>(type: "text", nullable: false),
                    banner_image_url = table.Column<string>(type: "text", nullable: true),
                    extra_description = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_launcher_download_mirrors", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "launcher_launcher_versions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    version = table.Column<string>(type: "text", nullable: false),
                    latest = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_launcher_launcher_versions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "launcher_thrive_versions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    release_number = table.Column<string>(type: "text", nullable: false),
                    stable = table.Column<bool>(type: "boolean", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    supports_failed_startup_detection = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_launcher_thrive_versions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "launcher_version_auto_update_channels",
                columns: table => new
                {
                    version_id = table.Column<long>(type: "bigint", nullable: false),
                    channel = table.Column<int>(type: "integer", nullable: false),
                    file_sha3 = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_launcher_version_auto_update_channels", x => new { x.version_id, x.channel });
                    table.ForeignKey(
                        name: "fk_launcher_version_auto_update_channels_launcher_launcher_ver",
                        column: x => x.version_id,
                        principalTable: "launcher_launcher_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "launcher_thrive_version_platforms",
                columns: table => new
                {
                    version_id = table.Column<long>(type: "bigint", nullable: false),
                    platform = table.Column<int>(type: "integer", nullable: false),
                    file_sha3 = table.Column<string>(type: "text", nullable: false),
                    local_file_name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_launcher_thrive_version_platforms", x => new { x.version_id, x.platform });
                    table.ForeignKey(
                        name: "fk_launcher_thrive_version_platforms_launcher_thrive_versions_",
                        column: x => x.version_id,
                        principalTable: "launcher_thrive_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "launcher_version_downloads",
                columns: table => new
                {
                    version_id = table.Column<long>(type: "bigint", nullable: false),
                    channel = table.Column<int>(type: "integer", nullable: false),
                    mirror_id = table.Column<long>(type: "bigint", nullable: false),
                    download_url = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_launcher_version_downloads", x => new { x.version_id, x.channel, x.mirror_id });
                    table.ForeignKey(
                        name: "fk_launcher_version_downloads_launcher_download_mirrors_mirror",
                        column: x => x.mirror_id,
                        principalTable: "launcher_download_mirrors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_launcher_version_downloads_launcher_launcher_versions_versi",
                        column: x => x.version_id,
                        principalTable: "launcher_launcher_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_launcher_version_downloads_launcher_version_auto_update_cha",
                        columns: x => new { x.version_id, x.channel },
                        principalTable: "launcher_version_auto_update_channels",
                        principalColumns: new[] { "version_id", "channel" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "launcher_thrive_version_downloads",
                columns: table => new
                {
                    version_id = table.Column<long>(type: "bigint", nullable: false),
                    platform = table.Column<int>(type: "integer", nullable: false),
                    mirror_id = table.Column<long>(type: "bigint", nullable: false),
                    download_url = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_launcher_thrive_version_downloads", x => new { x.version_id, x.platform, x.mirror_id });
                    table.ForeignKey(
                        name: "fk_launcher_thrive_version_downloads_launcher_download_mirrors",
                        column: x => x.mirror_id,
                        principalTable: "launcher_download_mirrors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_launcher_thrive_version_downloads_launcher_thrive_version_p",
                        columns: x => new { x.version_id, x.platform },
                        principalTable: "launcher_thrive_version_platforms",
                        principalColumns: new[] { "version_id", "platform" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_launcher_thrive_version_downloads_launcher_thrive_versions_",
                        column: x => x.version_id,
                        principalTable: "launcher_thrive_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_launcher_launcher_versions_version",
                table: "launcher_launcher_versions",
                column: "version",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_launcher_thrive_version_downloads_mirror_id",
                table: "launcher_thrive_version_downloads",
                column: "mirror_id");

            migrationBuilder.CreateIndex(
                name: "ix_launcher_thrive_versions_release_number",
                table: "launcher_thrive_versions",
                column: "release_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_launcher_version_downloads_mirror_id",
                table: "launcher_version_downloads",
                column: "mirror_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "launcher_thrive_version_downloads");

            migrationBuilder.DropTable(
                name: "launcher_version_downloads");

            migrationBuilder.DropTable(
                name: "launcher_thrive_version_platforms");

            migrationBuilder.DropTable(
                name: "launcher_download_mirrors");

            migrationBuilder.DropTable(
                name: "launcher_version_auto_update_channels");

            migrationBuilder.DropTable(
                name: "launcher_thrive_versions");

            migrationBuilder.DropTable(
                name: "launcher_launcher_versions");
        }
    }
}
