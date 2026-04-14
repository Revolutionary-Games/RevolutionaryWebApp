using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    /// <inheritdoc />
    public partial class RemoveOldServers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "external_servers");

            migrationBuilder.DropIndex(
                name: "ix_ci_jobs_hashed_build_output_connect_key",
                table: "ci_jobs");

            migrationBuilder.DropColumn(
                name: "reservation_type",
                table: "controlled_servers");

            migrationBuilder.DropColumn(
                name: "reserved_for",
                table: "controlled_servers");

            migrationBuilder.DropColumn(
                name: "build_output_connect_key",
                table: "ci_jobs");

            migrationBuilder.DropColumn(
                name: "hashed_build_output_connect_key",
                table: "ci_jobs");

            migrationBuilder.DropColumn(
                name: "running_on_server_id",
                table: "ci_jobs");

            migrationBuilder.DropColumn(
                name: "running_on_server_is_external",
                table: "ci_jobs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "reservation_type",
                table: "controlled_servers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "reserved_for",
                table: "controlled_servers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "build_output_connect_key",
                table: "ci_jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "hashed_build_output_connect_key",
                table: "ci_jobs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "running_on_server_id",
                table: "ci_jobs",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "running_on_server_is_external",
                table: "ci_jobs",
                type: "boolean",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "external_servers",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    clean_up_queued = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_maintenance = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    provisioned_fully = table.Column<bool>(type: "boolean", nullable: false),
                    public_address = table.Column<string>(type: "text", nullable: false),
                    reservation_type = table.Column<int>(type: "integer", nullable: false),
                    reserved_for = table.Column<long>(type: "bigint", nullable: true),
                    running_since = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ssh_key_file_name = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    status_last_checked = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    used_disk_space = table.Column<int>(type: "integer", nullable: false, defaultValue: -1),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    wants_maintenance = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_external_servers", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ci_jobs_hashed_build_output_connect_key",
                table: "ci_jobs",
                column: "hashed_build_output_connect_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_external_servers_public_address",
                table: "external_servers",
                column: "public_address",
                unique: true);
        }
    }
}
