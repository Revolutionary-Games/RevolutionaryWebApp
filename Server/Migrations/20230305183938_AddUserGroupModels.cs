using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ThriveDevCenter.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddUserGroupModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_groups",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_groups", x => x.id);
                    table.CheckConstraint("id_validity_check", "id > 0 AND id != 5 AND id != 10000 AND id != 2");
                });

            migrationBuilder.CreateTable(
                name: "user_groups_extra_data",
                columns: table => new
                {
                    group_id = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    custom_description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_groups_extra_data", x => x.group_id);
                    table.ForeignKey(
                        name: "fk_user_groups_extra_data_user_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "user_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_user_group",
                columns: table => new
                {
                    groups_id = table.Column<int>(type: "integer", nullable: false),
                    members_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_user_group", x => new { x.groups_id, x.members_id });
                    table.ForeignKey(
                        name: "fk_user_user_group_user_groups_groups_id",
                        column: x => x.groups_id,
                        principalTable: "user_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_user_group_users_members_id",
                        column: x => x.members_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "user_groups",
                columns: new[] { "id", "name" },
                values: new object[,]
                {
                    { 1, "RestrictedUser" },
                    { 3, "Developer" },
                    { 4, "Admin" }
                });

            migrationBuilder.InsertData(
                table: "user_groups_extra_data",
                columns: new[] { "group_id", "created_at", "custom_description", "updated_at" },
                values: new object[,]
                {
                    { 1, new DateTime(2023, 3, 5, 18, 39, 38, 313, DateTimeKind.Utc).AddTicks(4002), "Inbuilt group, cannot be modified", new DateTime(2023, 3, 5, 18, 39, 38, 313, DateTimeKind.Utc).AddTicks(4002) },
                    { 3, new DateTime(2023, 3, 5, 18, 39, 38, 313, DateTimeKind.Utc).AddTicks(4002), "Inbuilt group, cannot be modified", new DateTime(2023, 3, 5, 18, 39, 38, 313, DateTimeKind.Utc).AddTicks(4002) },
                    { 4, new DateTime(2023, 3, 5, 18, 39, 38, 313, DateTimeKind.Utc).AddTicks(4002), "Inbuilt group, cannot be modified", new DateTime(2023, 3, 5, 18, 39, 38, 313, DateTimeKind.Utc).AddTicks(4002) }
                });

            migrationBuilder.CreateIndex(
                name: "ix_user_user_group_members_id",
                table: "user_user_group",
                column: "members_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_groups_extra_data");

            migrationBuilder.DropTable(
                name: "user_user_group");

            migrationBuilder.DropTable(
                name: "user_groups");
        }
    }
}
