using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddNewRemoteRunnerProperties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ran_on_server",
                table: "ci_jobs",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "job_name",
                table: "ci_jobs",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "image",
                table: "ci_jobs",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "cache_settings_json",
                table: "ci_jobs",
                type: "character varying(8096)",
                maxLength: 8096,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "output_connection",
                table: "ci_jobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "required_runner_tags",
                table: "ci_jobs",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "reserved_by_runner_id",
                table: "ci_jobs",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "ci_jobs",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "remote_runners",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    tags = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    access_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hashed_access_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    secret_key = table.Column<Guid>(type: "uuid", nullable: false),
                    last_heartbeat = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_ip_address = table.Column<IPAddress>(type: "inet", nullable: true),
                    current_connection_id = table.Column<int>(type: "integer", nullable: false),
                    last_triggered_error = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    disallow_jobs = table.Column<bool>(type: "boolean", nullable: false),
                    queued_clean_up = table.Column<bool>(type: "boolean", nullable: false),
                    total_jobs_taken = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_remote_runners", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ci_jobs_created_at",
                table: "ci_jobs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_ci_jobs_reserved_by_runner_id",
                table: "ci_jobs",
                column: "reserved_by_runner_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_ci_jobs_remote_runners_reserved_by_runner_id",
                table: "ci_jobs",
                column: "reserved_by_runner_id",
                principalTable: "remote_runners",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_ci_jobs_remote_runners_reserved_by_runner_id",
                table: "ci_jobs");

            migrationBuilder.DropTable(
                name: "remote_runners");

            migrationBuilder.DropIndex(
                name: "ix_ci_jobs_created_at",
                table: "ci_jobs");

            migrationBuilder.DropIndex(
                name: "ix_ci_jobs_reserved_by_runner_id",
                table: "ci_jobs");

            migrationBuilder.DropColumn(
                name: "output_connection",
                table: "ci_jobs");

            migrationBuilder.DropColumn(
                name: "required_runner_tags",
                table: "ci_jobs");

            migrationBuilder.DropColumn(
                name: "reserved_by_runner_id",
                table: "ci_jobs");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "ci_jobs");

            migrationBuilder.AlterColumn<string>(
                name: "ran_on_server",
                table: "ci_jobs",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "job_name",
                table: "ci_jobs",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);

            migrationBuilder.AlterColumn<string>(
                name: "image",
                table: "ci_jobs",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "cache_settings_json",
                table: "ci_jobs",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(8096)",
                oldMaxLength: 8096,
                oldNullable: true);
        }
    }
}
