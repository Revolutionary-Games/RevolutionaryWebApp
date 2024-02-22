using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace RevolutionaryWebApp.Server.Migrations
{
    public partial class AddExternalServerModel : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "external_servers",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ssh_key_file_name = table.Column<string>(type: "text", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    status_last_checked = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    reservation_type = table.Column<int>(type: "integer", nullable: false),
                    reserved_for = table.Column<long>(type: "bigint", nullable: true),
                    public_address = table.Column<string>(type: "text", nullable: false),
                    running_since = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    provisioned_fully = table.Column<bool>(type: "boolean", nullable: false),
                    used_disk_space = table.Column<int>(type: "integer", nullable: false, defaultValue: -1),
                    clean_up_queued = table.Column<bool>(type: "boolean", nullable: false),
                    wants_maintenance = table.Column<bool>(type: "boolean", nullable: false),
                    last_maintenance = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_external_servers", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_external_servers_public_address",
                table: "external_servers",
                column: "public_address",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "external_servers");
        }
    }
}
