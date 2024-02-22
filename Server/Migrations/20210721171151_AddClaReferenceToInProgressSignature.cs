using Microsoft.EntityFrameworkCore.Migrations;

namespace RevolutionaryWebApp.Server.Migrations
{
    public partial class AddClaReferenceToInProgressSignature : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "cla_id",
                table: "in_progress_cla_signatures",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "ix_in_progress_cla_signatures_cla_id",
                table: "in_progress_cla_signatures",
                column: "cla_id");

            migrationBuilder.AddForeignKey(
                name: "fk_in_progress_cla_signatures_clas_cla_id",
                table: "in_progress_cla_signatures",
                column: "cla_id",
                principalTable: "clas",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_in_progress_cla_signatures_clas_cla_id",
                table: "in_progress_cla_signatures");

            migrationBuilder.DropIndex(
                name: "ix_in_progress_cla_signatures_cla_id",
                table: "in_progress_cla_signatures");

            migrationBuilder.DropColumn(
                name: "cla_id",
                table: "in_progress_cla_signatures");
        }
    }
}
