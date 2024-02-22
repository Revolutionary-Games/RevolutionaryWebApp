using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenanceOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "executed_maintenance_operations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    operation_type = table.Column<string>(type: "text", nullable: false),
                    extended_description = table.Column<string>(type: "text", nullable: true),
                    finished_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    performed_by_id = table.Column<long>(type: "bigint", nullable: true),
                    failed = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_executed_maintenance_operations", x => x.id);
                    table.ForeignKey(
                        name: "fk_executed_maintenance_operations_users_performed_by_id",
                        column: x => x.performed_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_executed_maintenance_operations_operation_type",
                table: "executed_maintenance_operations",
                column: "operation_type");

            migrationBuilder.CreateIndex(
                name: "ix_executed_maintenance_operations_performed_by_id",
                table: "executed_maintenance_operations",
                column: "performed_by_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "executed_maintenance_operations");
        }
    }
}
