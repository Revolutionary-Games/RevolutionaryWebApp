using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace ThriveDevCenter.Server.Migrations
{
    public partial class AddControlledServer : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "controlled_servers",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    status = table.Column<int>(type: "integer", nullable: false),
                    status_last_checked = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    reservation_type = table.Column<int>(type: "integer", nullable: false),
                    reserved_for = table.Column<long>(type: "bigint", nullable: true),
                    public_address = table.Column<IPAddress>(type: "inet", nullable: true),
                    running_since = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    total_runtime = table.Column<double>(type: "double precision", nullable: false),
                    created_with_image = table.Column<string>(type: "text", nullable: true),
                    aws_instance_type = table.Column<string>(type: "text", nullable: true),
                    created_volume_size = table.Column<long>(type: "bigint", nullable: false),
                    provisioned_fully = table.Column<bool>(type: "boolean", nullable: false),
                    instance_id = table.Column<string>(type: "text", nullable: true),
                    available_disk_space = table.Column<long>(type: "bigint", nullable: false),
                    wants_maintenance = table.Column<bool>(type: "boolean", nullable: false),
                    last_maintenance = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_controlled_servers", x => x.id);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "controlled_servers");
        }
    }
}
