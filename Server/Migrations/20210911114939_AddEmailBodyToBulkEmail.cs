using Microsoft.EntityFrameworkCore.Migrations;

namespace RevolutionaryWebApp.Server.Migrations
{
    public partial class AddEmailBodyToBulkEmail : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "html_body",
                table: "sent_bulk_emails",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "plain_body",
                table: "sent_bulk_emails",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "html_body",
                table: "sent_bulk_emails");

            migrationBuilder.DropColumn(
                name: "plain_body",
                table: "sent_bulk_emails");
        }
    }
}
