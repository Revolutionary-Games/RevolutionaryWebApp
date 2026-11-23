using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    /// <inheritdoc />
    public partial class SplitModerationRegistrationBlocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "normalized_email",
                table: "registration_blocks",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(320)",
                oldMaxLength: 320);

            migrationBuilder.AlterColumn<string>(
                name: "email",
                table: "registration_blocks",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(320)",
                oldMaxLength: 320);

            migrationBuilder.AddColumn<long>(
                name: "moderation_action_id",
                table: "registration_blocks",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_registration_blocks_moderation_action_id",
                table: "registration_blocks",
                column: "moderation_action_id");

            migrationBuilder.AddForeignKey(
                name: "fk_registration_blocks_pending_user_moderation_moderation_acti",
                table: "registration_blocks",
                column: "moderation_action_id",
                principalTable: "pending_user_moderation",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_registration_blocks_pending_user_moderation_moderation_acti",
                table: "registration_blocks");

            migrationBuilder.DropIndex(
                name: "ix_registration_blocks_moderation_action_id",
                table: "registration_blocks");

            migrationBuilder.DropColumn(
                name: "moderation_action_id",
                table: "registration_blocks");

            migrationBuilder.AlterColumn<string>(
                name: "normalized_email",
                table: "registration_blocks",
                type: "character varying(320)",
                maxLength: 320,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(320)",
                oldMaxLength: 320,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "email",
                table: "registration_blocks",
                type: "character varying(320)",
                maxLength: 320,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(320)",
                oldMaxLength: 320,
                oldNullable: true);
        }
    }
}
