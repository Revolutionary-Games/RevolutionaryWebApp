using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace RevolutionaryWebApp.Server.Migrations
{
    public partial class RemoveUnnecessaryClaFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_in_progress_cla_signatures_email_verification_code",
                table: "in_progress_cla_signatures");

            migrationBuilder.DropColumn(
                name: "email_verification_code",
                table: "in_progress_cla_signatures");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "email_verification_code",
                table: "in_progress_cla_signatures",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_in_progress_cla_signatures_email_verification_code",
                table: "in_progress_cla_signatures",
                column: "email_verification_code",
                unique: true);
        }
    }
}
