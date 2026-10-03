using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    /// <inheritdoc />
    public partial class UserNotificationCompositeKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_user_notifications",
                table: "user_notifications");

            migrationBuilder.AddPrimaryKey(
                name: "pk_user_notifications",
                table: "user_notifications",
                columns: new[] { "user_id", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_user_notifications",
                table: "user_notifications");

            migrationBuilder.AddPrimaryKey(
                name: "pk_user_notifications",
                table: "user_notifications",
                column: "id");
        }
    }
}
