using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ThriveDevCenter.Server.Migrations
{
    public partial class AddStackwalkTask : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stackwalk_tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    hashed_id = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    finished_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    dump_temp_category = table.Column<string>(type: "text", nullable: false),
                    dump_file_name = table.Column<string>(type: "text", nullable: false),
                    delete_dump_after_running = table.Column<bool>(type: "boolean", nullable: false),
                    special_walk_type = table.Column<string>(type: "text", nullable: true),
                    result = table.Column<string>(type: "text", nullable: true),
                    succeeded = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stackwalk_tasks", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stackwalk_tasks_hashed_id",
                table: "stackwalk_tasks",
                column: "hashed_id",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stackwalk_tasks");
        }
    }
}
