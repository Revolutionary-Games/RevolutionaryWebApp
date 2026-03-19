using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    /// <inheritdoc />
    public partial class DeleteOldPageVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // We intentionally delete *everything* as the data is simply not compatible!
            migrationBuilder.Sql("-- noinspection SqlWithoutWhereForFile\n"+
            "DELETE FROM page_versions;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
