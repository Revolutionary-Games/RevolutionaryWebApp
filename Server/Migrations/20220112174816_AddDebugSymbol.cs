using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace ThriveDevCenter.Server.Migrations
{
    public partial class AddDebugSymbol : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "debug_symbols",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: false),
                    relative_path = table.Column<string>(type: "text", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    uploaded = table.Column<bool>(type: "boolean", nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    stored_in_item_id = table.Column<long>(type: "bigint", nullable: false),
                    created_by_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_debug_symbols", x => x.id);
                    table.ForeignKey(
                        name: "fk_debug_symbols_storage_items_stored_in_item_id",
                        column: x => x.stored_in_item_id,
                        principalTable: "storage_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_debug_symbols_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_debug_symbols_created_by_id",
                table: "debug_symbols",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_debug_symbols_relative_path",
                table: "debug_symbols",
                column: "relative_path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_debug_symbols_stored_in_item_id",
                table: "debug_symbols",
                column: "stored_in_item_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "debug_symbols");
        }
    }
}
