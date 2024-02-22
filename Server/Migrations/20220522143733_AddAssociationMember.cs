using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    public partial class AddAssociationMember : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "association_member",
                table: "users");

            migrationBuilder.DropColumn(
                name: "board_member",
                table: "users");

            migrationBuilder.DropColumn(
                name: "has_been_board_member",
                table: "users");

            migrationBuilder.CreateTable(
                name: "association_members",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    first_names = table.Column<string>(type: "text", nullable: false),
                    last_name = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    join_date = table.Column<DateOnly>(type: "date", nullable: false),
                    country_of_residence = table.Column<string>(type: "text", nullable: false),
                    city_of_residence = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: true),
                    board_member = table.Column<bool>(type: "boolean", nullable: false),
                    has_been_board_member = table.Column<bool>(type: "boolean", nullable: false),
                    is_thrive_developer = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_association_members", x => x.id);
                    table.ForeignKey(
                        name: "fk_association_members_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_association_members_email",
                table: "association_members",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_association_members_user_id",
                table: "association_members",
                column: "user_id",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "association_members");

            migrationBuilder.AddColumn<bool>(
                name: "association_member",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "board_member",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "has_been_board_member",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
