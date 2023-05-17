using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThriveDevCenter.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddDeletedResourceStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "deleted_resource_stats",
                columns: table => new
                {
                    type = table.Column<int>(type: "integer", nullable: false),
                    item_count = table.Column<long>(type: "bigint", nullable: false),
                    items_extra_attribute = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_deleted_resource_stats", x => x.type);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "deleted_resource_stats");
        }
    }
}
