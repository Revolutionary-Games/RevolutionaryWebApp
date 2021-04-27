using Microsoft.EntityFrameworkCore.Migrations;

namespace ThriveDevCenter.Server.Migrations
{
    public partial class MakeSomeStorageItemPropsUnNullableMoreDevBuildIndexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "write_access",
                table: "storage_items",
                type: "integer",
                nullable: false,
                defaultValue: 2,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true,
                oldDefaultValue: 2);

            migrationBuilder.AlterColumn<int>(
                name: "read_access",
                table: "storage_items",
                type: "integer",
                nullable: false,
                defaultValue: 2,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true,
                oldDefaultValue: 2);

            migrationBuilder.AlterColumn<int>(
                name: "ftype",
                table: "storage_items",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_dev_builds_branch",
                table: "dev_builds",
                column: "branch");

            migrationBuilder.CreateIndex(
                name: "ix_dev_builds_platform",
                table: "dev_builds",
                column: "platform");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_dev_builds_branch",
                table: "dev_builds");

            migrationBuilder.DropIndex(
                name: "ix_dev_builds_platform",
                table: "dev_builds");

            migrationBuilder.AlterColumn<int>(
                name: "write_access",
                table: "storage_items",
                type: "integer",
                nullable: true,
                defaultValue: 2,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 2);

            migrationBuilder.AlterColumn<int>(
                name: "read_access",
                table: "storage_items",
                type: "integer",
                nullable: true,
                defaultValue: 2,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 2);

            migrationBuilder.AlterColumn<int>(
                name: "ftype",
                table: "storage_items",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");
        }
    }
}
