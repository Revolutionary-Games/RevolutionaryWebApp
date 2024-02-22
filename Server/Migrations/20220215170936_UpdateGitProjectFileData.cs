using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RevolutionaryWebApp.Server.Migrations
{
    public partial class UpdateGitProjectFileData : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_project_git_files_lfs_project_id_name_path",
                table: "project_git_files");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "project_git_files");

            migrationBuilder.DropColumn(
                name: "ftype",
                table: "project_git_files");

            migrationBuilder.AddColumn<int>(
                name: "f_type",
                table: "project_git_files",
                type: "integer",
                nullable: false);

            migrationBuilder.AddColumn<string>(
                name: "branch_to_build_file_tree_for",
                table: "lfs_projects",
                type: "text",
                nullable: false,
                defaultValue: "master");

            migrationBuilder.CreateIndex(
                name: "ix_project_git_files_lfs_project_id_path_name",
                table: "project_git_files",
                columns: new[] { "lfs_project_id", "path", "name" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_project_git_files_lfs_project_id_path_name",
                table: "project_git_files");

            migrationBuilder.DropColumn(
                name: "branch_to_build_file_tree_for",
                table: "lfs_projects");

            migrationBuilder.DropColumn(
                name: "f_type",
                table: "project_git_files");

            migrationBuilder.AddColumn<string>(
                name: "ftype",
                table: "project_git_files",
                type: "text",
                nullable: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at",
                table: "project_git_files",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateIndex(
                name: "ix_project_git_files_lfs_project_id_name_path",
                table: "project_git_files",
                columns: new[] { "lfs_project_id", "name", "path" },
                unique: true);
        }
    }
}
