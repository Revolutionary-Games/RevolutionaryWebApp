using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationBatchingAndPriority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "batch_key",
                table: "user_notifications",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "combined_count",
                table: "user_notifications",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<long>(
                name: "combined_into_id",
                table: "user_notifications",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "priority",
                table: "user_notifications",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "user_notifications",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateIndex(
                name: "ix_user_notifications_created_at_type",
                table: "user_notifications",
                columns: new[] { "created_at", "type" },
                filter: "read_at IS NULL AND combined_into_id IS NULL AND email_sent_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_user_notifications_user_id_combined_into_id",
                table: "user_notifications",
                columns: new[] { "user_id", "combined_into_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_user_notifications_user_notifications_user_id_combined_into",
                table: "user_notifications",
                columns: new[] { "user_id", "combined_into_id" },
                principalTable: "user_notifications",
                principalColumns: new[] { "user_id", "id" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_user_notifications_user_notifications_user_id_combined_into",
                table: "user_notifications");

            migrationBuilder.DropIndex(
                name: "ix_user_notifications_created_at_type",
                table: "user_notifications");

            migrationBuilder.DropIndex(
                name: "ix_user_notifications_user_id_combined_into_id",
                table: "user_notifications");

            migrationBuilder.DropColumn(
                name: "batch_key",
                table: "user_notifications");

            migrationBuilder.DropColumn(
                name: "combined_count",
                table: "user_notifications");

            migrationBuilder.DropColumn(
                name: "combined_into_id",
                table: "user_notifications");

            migrationBuilder.DropColumn(
                name: "priority",
                table: "user_notifications");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "user_notifications");
        }
    }
}
