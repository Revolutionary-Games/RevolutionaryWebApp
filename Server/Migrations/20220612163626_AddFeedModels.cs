using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    public partial class AddFeedModels : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "combined_feeds",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    html_feed_item_entry_template = table.Column<string>(type: "text", nullable: false),
                    cache_time = table.Column<TimeSpan>(type: "interval", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    max_items = table.Column<int>(type: "integer", nullable: false),
                    latest_content = table.Column<string>(type: "text", nullable: true),
                    content_updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_combined_feeds", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "feeds",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    url = table.Column<string>(type: "text", nullable: false),
                    poll_interval = table.Column<TimeSpan>(type: "interval", nullable: false),
                    cache_time = table.Column<TimeSpan>(type: "interval", nullable: true),
                    html_feed_item_entry_template = table.Column<string>(type: "text", nullable: true),
                    html_feed_version_suffix = table.Column<string>(type: "text", nullable: true),
                    html_latest_content = table.Column<string>(type: "text", nullable: true),
                    max_item_length = table.Column<int>(type: "integer", nullable: false),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    max_items = table.Column<int>(type: "integer", nullable: false),
                    latest_content = table.Column<string>(type: "text", nullable: true),
                    content_updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feeds", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "combined_feed_feed",
                columns: table => new
                {
                    combined_from_feeds_id = table.Column<long>(type: "bigint", nullable: false),
                    combined_into_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_combined_feed_feed", x => new { x.combined_from_feeds_id, x.combined_into_id });
                    table.ForeignKey(
                        name: "fk_combined_feed_feed_combined_feeds_combined_into_id",
                        column: x => x.combined_into_id,
                        principalTable: "combined_feeds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_combined_feed_feed_feeds_combined_from_feeds_id",
                        column: x => x.combined_from_feeds_id,
                        principalTable: "feeds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "feed_discord_webhooks",
                columns: table => new
                {
                    feed_id = table.Column<long>(type: "bigint", nullable: false),
                    webhook_url = table.Column<string>(type: "text", nullable: false),
                    custom_item_format = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feed_discord_webhooks", x => new { x.feed_id, x.webhook_url });
                    table.ForeignKey(
                        name: "fk_feed_discord_webhooks_feeds_feed_id",
                        column: x => x.feed_id,
                        principalTable: "feeds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "seen_feed_items",
                columns: table => new
                {
                    feed_id = table.Column<long>(type: "bigint", nullable: false),
                    item_identifier = table.Column<string>(type: "text", nullable: false),
                    seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seen_feed_items", x => new { x.feed_id, x.item_identifier });
                    table.ForeignKey(
                        name: "fk_seen_feed_items_feeds_feed_id",
                        column: x => x.feed_id,
                        principalTable: "feeds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_combined_feed_feed_combined_into_id",
                table: "combined_feed_feed",
                column: "combined_into_id");

            migrationBuilder.CreateIndex(
                name: "ix_combined_feeds_name",
                table: "combined_feeds",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_feeds_content_updated_at",
                table: "feeds",
                column: "content_updated_at");

            migrationBuilder.CreateIndex(
                name: "ix_feeds_name",
                table: "feeds",
                column: "name",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "combined_feed_feed");

            migrationBuilder.DropTable(
                name: "feed_discord_webhooks");

            migrationBuilder.DropTable(
                name: "seen_feed_items");

            migrationBuilder.DropTable(
                name: "combined_feeds");

            migrationBuilder.DropTable(
                name: "feeds");
        }
    }
}
