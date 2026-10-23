using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    /// <inheritdoc />
    public partial class StoreUserNotificationTypeAsInt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "type",
                table: "user_notifications");

            migrationBuilder.AddColumn<int>(
                name: "type",
                table: "user_notifications",
                type: "integer",
                nullable: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "type",
                table: "user_notifications");

            migrationBuilder.AddColumn<string>(
                name: "type",
                table: "user_notifications",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false);
        }
    }
}
