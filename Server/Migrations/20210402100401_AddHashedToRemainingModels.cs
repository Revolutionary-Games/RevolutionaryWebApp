using Microsoft.EntityFrameworkCore.Migrations;

namespace RevolutionaryWebApp.Server.Migrations
{
    public partial class AddHashedToRemainingModels : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_api_token",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_users_launcher_link_code",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_users_lfs_token",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_project_git_files_lfs_project_id",
                table: "project_git_files");

            migrationBuilder.DropIndex(
                name: "ix_project_git_files_path_name_lfs_project_id",
                table: "project_git_files");

            migrationBuilder.DropIndex(
                name: "ix_launcher_links_link_code",
                table: "launcher_links");

            migrationBuilder.DropIndex(
                name: "ix_access_keys_key_code",
                table: "access_keys");

            migrationBuilder.AddColumn<string>(
                name: "hashed_api_token",
                table: "users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "hashed_launcher_link_code",
                table: "users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "hashed_lfs_token",
                table: "users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "hashed_id",
                table: "redeemable_codes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "hashed_link_code",
                table: "launcher_links",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "hashed_key_code",
                table: "access_keys",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_hashed_api_token",
                table: "users",
                column: "hashed_api_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_hashed_launcher_link_code",
                table: "users",
                column: "hashed_launcher_link_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_hashed_lfs_token",
                table: "users",
                column: "hashed_lfs_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_redeemable_codes_hashed_id",
                table: "redeemable_codes",
                column: "hashed_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_project_git_files_lfs_project_id_name_path",
                table: "project_git_files",
                columns: new[] { "lfs_project_id", "name", "path" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_launcher_links_hashed_link_code",
                table: "launcher_links",
                column: "hashed_link_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_access_keys_hashed_key_code",
                table: "access_keys",
                column: "hashed_key_code",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_hashed_api_token",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_users_hashed_launcher_link_code",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_users_hashed_lfs_token",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_redeemable_codes_hashed_id",
                table: "redeemable_codes");

            migrationBuilder.DropIndex(
                name: "ix_project_git_files_lfs_project_id_name_path",
                table: "project_git_files");

            migrationBuilder.DropIndex(
                name: "ix_launcher_links_hashed_link_code",
                table: "launcher_links");

            migrationBuilder.DropIndex(
                name: "ix_access_keys_hashed_key_code",
                table: "access_keys");

            migrationBuilder.DropColumn(
                name: "hashed_api_token",
                table: "users");

            migrationBuilder.DropColumn(
                name: "hashed_launcher_link_code",
                table: "users");

            migrationBuilder.DropColumn(
                name: "hashed_lfs_token",
                table: "users");

            migrationBuilder.DropColumn(
                name: "hashed_id",
                table: "redeemable_codes");

            migrationBuilder.DropColumn(
                name: "hashed_link_code",
                table: "launcher_links");

            migrationBuilder.DropColumn(
                name: "hashed_key_code",
                table: "access_keys");

            migrationBuilder.CreateIndex(
                name: "ix_users_api_token",
                table: "users",
                column: "api_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_launcher_link_code",
                table: "users",
                column: "launcher_link_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_lfs_token",
                table: "users",
                column: "lfs_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_project_git_files_lfs_project_id",
                table: "project_git_files",
                column: "lfs_project_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_git_files_path_name_lfs_project_id",
                table: "project_git_files",
                columns: new[] { "path", "name", "lfs_project_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_launcher_links_link_code",
                table: "launcher_links",
                column: "link_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_access_keys_key_code",
                table: "access_keys",
                column: "key_code",
                unique: true);
        }
    }
}
