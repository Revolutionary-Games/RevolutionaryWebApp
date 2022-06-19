using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThriveDevCenter.Server.Migrations
{
    public partial class AddFeedContentHash : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "latest_content_hash",
                table: "feeds",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_feed_discord_webhooks_feed_id_webhook_url",
                table: "feed_discord_webhooks",
                columns: new[] { "feed_id", "webhook_url" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_feed_discord_webhooks_feed_id_webhook_url",
                table: "feed_discord_webhooks");

            migrationBuilder.DropColumn(
                name: "latest_content_hash",
                table: "feeds");
        }
    }
}
