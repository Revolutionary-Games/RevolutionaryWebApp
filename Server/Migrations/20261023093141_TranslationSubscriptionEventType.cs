using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    /// <inheritdoc />
    public partial class TranslationSubscriptionEventType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_translation_subscriptions",
                table: "translation_subscriptions");

            migrationBuilder.DropColumn(
                name: "event_type",
                table: "translation_subscriptions");

            migrationBuilder.AddColumn<int>(
                name: "event_type",
                table: "translation_subscriptions",
                type: "integer",
                nullable: false);

            migrationBuilder.AddPrimaryKey(
                name: "pk_translation_subscriptions",
                table: "translation_subscriptions",
                columns: new[] { "user_id", "project_id", "language_code", "event_type" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_translation_subscriptions",
                table: "translation_subscriptions");

            migrationBuilder.DropColumn(
                name: "event_type",
                table: "translation_subscriptions");

            migrationBuilder.AddColumn<string>(
                name: "event_type",
                table: "translation_subscriptions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false);

            migrationBuilder.AddPrimaryKey(
                name: "pk_translation_subscriptions",
                table: "translation_subscriptions",
                columns: new[] { "user_id", "project_id", "language_code", "event_type" });
        }
    }
}
