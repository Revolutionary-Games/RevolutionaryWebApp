using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations;

public partial class PatreonV2 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn("reward_id", "patrons", "tier_id");
        migrationBuilder.RenameColumn("devbuilds_reward_id", "patreon_settings", "devbuilds_tier_id");
        migrationBuilder.RenameColumn("vip_reward_id", "patreon_settings", "vip_tier_id");

        migrationBuilder.AddColumn<string>(
            name: "entitled_tier_ids",
            table: "patrons",
            type: "text",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "patreon_member_id",
            table: "patrons",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "patreon_user_id",
            table: "patrons",
            type: "text",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_patrons_patreon_member_id",
            table: "patrons",
            column: "patreon_member_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_patrons_patreon_user_id",
            table: "patrons",
            column: "patreon_user_id",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "ix_patrons_patreon_member_id", table: "patrons");
        migrationBuilder.DropIndex(name: "ix_patrons_patreon_user_id", table: "patrons");
        migrationBuilder.DropColumn(name: "entitled_tier_ids", table: "patrons");
        migrationBuilder.DropColumn(name: "patreon_member_id", table: "patrons");
        migrationBuilder.DropColumn(name: "patreon_user_id", table: "patrons");
        migrationBuilder.RenameColumn("tier_id", "patrons", "reward_id");
        migrationBuilder.RenameColumn("devbuilds_tier_id", "patreon_settings", "devbuilds_reward_id");
        migrationBuilder.RenameColumn("vip_tier_id", "patreon_settings", "vip_reward_id");
    }
}
