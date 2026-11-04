using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationInboxIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_user_notifications_user_id",
                table: "user_notifications",
                column: "user_id",
                filter: "read_at IS NULL AND combined_into_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_user_notifications_user_id_created_at_id",
                table: "user_notifications",
                columns: new[] { "user_id", "created_at", "id" },
                descending: new[] { false, true, true },
                filter: "combined_into_id IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_user_notifications_user_id",
                table: "user_notifications");

            migrationBuilder.DropIndex(
                name: "ix_user_notifications_user_id_created_at_id",
                table: "user_notifications");
        }
    }
}
