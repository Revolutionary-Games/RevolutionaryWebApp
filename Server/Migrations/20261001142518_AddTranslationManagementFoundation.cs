using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace RevolutionaryWebApp.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTranslationManagementFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "use_email_in_translation_credits",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "translation_dictionaries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_translation_dictionaries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_notifications",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    deduplication_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    target_path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    email_sent_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_notifications", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_notifications_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "dictionary_term_requests",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    dictionary_id = table.Column<long>(type: "bigint", nullable: false),
                    word = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    context = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    explanation = table.Column<string>(type: "text", nullable: true),
                    requester_user_id = table.Column<long>(type: "bigint", nullable: false),
                    approved = table.Column<bool>(type: "boolean", nullable: true),
                    decided_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    decided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dictionary_term_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_dictionary_term_requests_translation_dictionaries_dictionar",
                        column: x => x.dictionary_id,
                        principalTable: "translation_dictionaries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dictionary_term_requests_users_decided_by_user_id",
                        column: x => x.decided_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_dictionary_term_requests_users_requester_user_id",
                        column: x => x.requester_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dictionary_terms",
                columns: table => new
                {
                    dictionary_id = table.Column<long>(type: "bigint", nullable: false),
                    word = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    context = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    explanation = table.Column<string>(type: "text", nullable: true),
                    last_edited_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dictionary_terms", x => new { x.dictionary_id, x.word, x.context });
                    table.ForeignKey(
                        name: "fk_dictionary_terms_translation_dictionaries_dictionary_id",
                        column: x => x.dictionary_id,
                        principalTable: "translation_dictionaries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dictionary_terms_users_last_edited_by_user_id",
                        column: x => x.last_edited_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "translation_projects",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    slug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    source_language = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: false),
                    format = table.Column<int>(type: "integer", nullable: false),
                    dictionary_id = table.Column<long>(type: "bigint", nullable: true),
                    repository_full_name = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    source_branch = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    source_file_pattern = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    translation_file_pattern = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    last_imported_sha = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    last_imported_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    current_revision = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_translation_projects", x => x.id);
                    table.ForeignKey(
                        name: "fk_translation_projects_translation_dictionaries_dictionary_id",
                        column: x => x.dictionary_id,
                        principalTable: "translation_dictionaries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dictionary_term_translations",
                columns: table => new
                {
                    dictionary_id = table.Column<long>(type: "bigint", nullable: false),
                    word = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    context = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    language_code = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    explanation = table.Column<string>(type: "text", nullable: true),
                    state = table.Column<int>(type: "integer", nullable: false),
                    last_edited_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dictionary_term_translations", x => new { x.dictionary_id, x.word, x.context, x.language_code });
                    table.ForeignKey(
                        name: "fk_dictionary_term_translations_dictionary_terms_dictionary_id",
                        columns: x => new { x.dictionary_id, x.word, x.context },
                        principalTable: "dictionary_terms",
                        principalColumns: new[] { "dictionary_id", "word", "context" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dictionary_term_translations_users_last_edited_by_user_id",
                        column: x => x.last_edited_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "translation_project_languages",
                columns: table => new
                {
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    language_code = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: false),
                    display_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    file_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    last_imported_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_translation_project_languages", x => new { x.project_id, x.language_code });
                    table.ForeignKey(
                        name: "fk_translation_project_languages_translation_projects_project_",
                        column: x => x.project_id,
                        principalTable: "translation_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "translation_pull_requests",
                columns: table => new
                {
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    head_branch = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    head_sha = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    open = table.Column<bool>(type: "boolean", nullable: false),
                    last_published_revision = table.Column<long>(type: "bigint", nullable: false),
                    last_checked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_translation_pull_requests", x => x.project_id);
                    table.ForeignKey(
                        name: "fk_translation_pull_requests_translation_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "translation_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "translation_sync_runs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    error = table.Column<string>(type: "text", nullable: true),
                    upstream_sha = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_translation_sync_runs", x => x.id);
                    table.ForeignKey(
                        name: "fk_translation_sync_runs_translation_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "translation_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "translation_units",
                columns: table => new
                {
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    key = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    source_text = table.Column<string>(type: "text", nullable: false),
                    source_plural_text = table.Column<string>(type: "text", nullable: true),
                    context = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    source_locations = table.Column<string>(type: "text", nullable: true),
                    source_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_revision = table.Column<long>(type: "bigint", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    source_updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_translation_units", x => new { x.project_id, x.key });
                    table.ForeignKey(
                        name: "fk_translation_units_translation_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "translation_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "translation_subscriptions",
                columns: table => new
                {
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    language_code = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: false),
                    event_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_translation_subscriptions", x => new { x.user_id, x.project_id, x.language_code, x.event_type });
                    table.ForeignKey(
                        name: "fk_translation_subscriptions_translation_project_languages_pro",
                        columns: x => new { x.project_id, x.language_code },
                        principalTable: "translation_project_languages",
                        principalColumns: new[] { "project_id", "language_code" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_translation_subscriptions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "translation_values",
                columns: table => new
                {
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    key = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    language_code = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    plural_forms_json = table.Column<string>(type: "text", nullable: true),
                    state = table.Column<int>(type: "integer", nullable: false),
                    source_revision_translated_from = table.Column<long>(type: "bigint", nullable: false),
                    source_text_translated_from = table.Column<string>(type: "text", nullable: false),
                    last_edited_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    last_edited_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<Guid>(type: "uuid", nullable: false),
                    pending_revision = table.Column<long>(type: "bigint", nullable: true),
                    pending_since = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_published_revision = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_translation_values", x => new { x.project_id, x.key, x.language_code });
                    table.ForeignKey(
                        name: "fk_translation_values_translation_project_languages_project_id",
                        columns: x => new { x.project_id, x.language_code },
                        principalTable: "translation_project_languages",
                        principalColumns: new[] { "project_id", "language_code" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_translation_values_translation_units_project_id_key",
                        columns: x => new { x.project_id, x.key },
                        principalTable: "translation_units",
                        principalColumns: new[] { "project_id", "key" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_translation_values_users_last_edited_by_user_id",
                        column: x => x.last_edited_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "translation_changes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    key = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    language_code = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: false),
                    actor_user_id = table.Column<long>(type: "bigint", nullable: true),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    old_text = table.Column<string>(type: "text", nullable: false),
                    new_text = table.Column<string>(type: "text", nullable: false),
                    old_state = table.Column<int>(type: "integer", nullable: false),
                    new_state = table.Column<int>(type: "integer", nullable: false),
                    source_revision = table.Column<long>(type: "bigint", nullable: false),
                    project_revision = table.Column<long>(type: "bigint", nullable: false),
                    counts_as_contribution = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_translation_changes", x => x.id);
                    table.ForeignKey(
                        name: "fk_translation_changes_translation_values_project_id_key_langu",
                        columns: x => new { x.project_id, x.key, x.language_code },
                        principalTable: "translation_values",
                        principalColumns: new[] { "project_id", "key", "language_code" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_translation_changes_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "translation_comments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    key = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    language_code = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: false),
                    author_user_id = table.Column<long>(type: "bigint", nullable: false),
                    raw_markdown = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    edited_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_translation_comments", x => x.id);
                    table.ForeignKey(
                        name: "fk_translation_comments_translation_values_project_id_key_lang",
                        columns: x => new { x.project_id, x.key, x.language_code },
                        principalTable: "translation_values",
                        principalColumns: new[] { "project_id", "key", "language_code" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_translation_comments_users_author_user_id",
                        column: x => x.author_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "translation_suggestions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    project_id = table.Column<long>(type: "bigint", nullable: false),
                    key = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    language_code = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: false),
                    author_user_id = table.Column<long>(type: "bigint", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    source_revision = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_translation_suggestions", x => x.id);
                    table.ForeignKey(
                        name: "fk_translation_suggestions_translation_values_project_id_key_l",
                        columns: x => new { x.project_id, x.key, x.language_code },
                        principalTable: "translation_values",
                        principalColumns: new[] { "project_id", "key", "language_code" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_translation_suggestions_users_author_user_id",
                        column: x => x.author_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "translation_suggestion_votes",
                columns: table => new
                {
                    suggestion_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    value = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_translation_suggestion_votes", x => new { x.suggestion_id, x.user_id });
                    table.CheckConstraint("translation_suggestion_vote_value_check", "value IN (-1, 1)");
                    table.ForeignKey(
                        name: "fk_translation_suggestion_votes_translation_suggestions_sugges",
                        column: x => x.suggestion_id,
                        principalTable: "translation_suggestions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_translation_suggestion_votes_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "user_groups",
                columns: new[] { "id", "name" },
                values: new object[,]
                {
                    { 15, "SourceTextTranslator" },
                    { 16, "TranslationVerifier" },
                    { 17, "DictionaryManager" }
                });

            migrationBuilder.InsertData(
                table: "user_groups_extra_data",
                columns: new[] { "group_id", "created_at", "custom_description", "updated_at" },
                values: new object[,]
                {
                    { 15, new DateTime(2023, 4, 2, 16, 4, 31, 355, DateTimeKind.Utc).AddTicks(4672), "Inbuilt group, cannot be modified", new DateTime(2023, 4, 2, 16, 4, 31, 355, DateTimeKind.Utc).AddTicks(4672) },
                    { 16, new DateTime(2023, 4, 2, 16, 4, 31, 355, DateTimeKind.Utc).AddTicks(4672), "Inbuilt group, cannot be modified", new DateTime(2023, 4, 2, 16, 4, 31, 355, DateTimeKind.Utc).AddTicks(4672) },
                    { 17, new DateTime(2023, 4, 2, 16, 4, 31, 355, DateTimeKind.Utc).AddTicks(4672), "Inbuilt group, cannot be modified", new DateTime(2023, 4, 2, 16, 4, 31, 355, DateTimeKind.Utc).AddTicks(4672) }
                });

            migrationBuilder.CreateIndex(
                name: "ix_dictionary_term_requests_decided_by_user_id",
                table: "dictionary_term_requests",
                column: "decided_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_dictionary_term_requests_dictionary_id_approved_created_at",
                table: "dictionary_term_requests",
                columns: new[] { "dictionary_id", "approved", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_dictionary_term_requests_requester_user_id",
                table: "dictionary_term_requests",
                column: "requester_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_dictionary_term_translations_last_edited_by_user_id",
                table: "dictionary_term_translations",
                column: "last_edited_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_dictionary_terms_last_edited_by_user_id",
                table: "dictionary_terms",
                column: "last_edited_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_translation_changes_actor_user_id",
                table: "translation_changes",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_translation_changes_project_id_key_language_code_created_at",
                table: "translation_changes",
                columns: new[] { "project_id", "key", "language_code", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_translation_comments_author_user_id",
                table: "translation_comments",
                column: "author_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_translation_comments_project_id_key_language_code_created_at",
                table: "translation_comments",
                columns: new[] { "project_id", "key", "language_code", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_translation_dictionaries_name",
                table: "translation_dictionaries",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_translation_projects_dictionary_id",
                table: "translation_projects",
                column: "dictionary_id");

            migrationBuilder.CreateIndex(
                name: "ix_translation_projects_repository_full_name_source_branch",
                table: "translation_projects",
                columns: new[] { "repository_full_name", "source_branch" });

            migrationBuilder.CreateIndex(
                name: "ix_translation_projects_slug",
                table: "translation_projects",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_translation_subscriptions_project_id_language_code",
                table: "translation_subscriptions",
                columns: new[] { "project_id", "language_code" });

            migrationBuilder.CreateIndex(
                name: "ix_translation_suggestion_votes_user_id",
                table: "translation_suggestion_votes",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_translation_suggestions_author_user_id",
                table: "translation_suggestions",
                column: "author_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_translation_suggestions_project_id_key_language_code",
                table: "translation_suggestions",
                columns: new[] { "project_id", "key", "language_code" });

            migrationBuilder.CreateIndex(
                name: "ix_translation_suggestions_status_created_at",
                table: "translation_suggestions",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_translation_sync_runs_project_id_started_at",
                table: "translation_sync_runs",
                columns: new[] { "project_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_translation_units_project_id_active",
                table: "translation_units",
                columns: new[] { "project_id", "active" });

            migrationBuilder.CreateIndex(
                name: "ix_translation_values_last_edited_by_user_id",
                table: "translation_values",
                column: "last_edited_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_translation_values_project_id_language_code_state",
                table: "translation_values",
                columns: new[] { "project_id", "language_code", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_translation_values_project_id_pending_since",
                table: "translation_values",
                columns: new[] { "project_id", "pending_since" });

            migrationBuilder.CreateIndex(
                name: "ix_user_notifications_user_id_deduplication_key",
                table: "user_notifications",
                columns: new[] { "user_id", "deduplication_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_notifications_user_id_read_at_created_at",
                table: "user_notifications",
                columns: new[] { "user_id", "read_at", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dictionary_term_requests");

            migrationBuilder.DropTable(
                name: "dictionary_term_translations");

            migrationBuilder.DropTable(
                name: "translation_changes");

            migrationBuilder.DropTable(
                name: "translation_comments");

            migrationBuilder.DropTable(
                name: "translation_pull_requests");

            migrationBuilder.DropTable(
                name: "translation_subscriptions");

            migrationBuilder.DropTable(
                name: "translation_suggestion_votes");

            migrationBuilder.DropTable(
                name: "translation_sync_runs");

            migrationBuilder.DropTable(
                name: "user_notifications");

            migrationBuilder.DropTable(
                name: "dictionary_terms");

            migrationBuilder.DropTable(
                name: "translation_suggestions");

            migrationBuilder.DropTable(
                name: "translation_values");

            migrationBuilder.DropTable(
                name: "translation_project_languages");

            migrationBuilder.DropTable(
                name: "translation_units");

            migrationBuilder.DropTable(
                name: "translation_projects");

            migrationBuilder.DropTable(
                name: "translation_dictionaries");

            migrationBuilder.DeleteData(
                table: "user_groups_extra_data",
                keyColumn: "group_id",
                keyValue: 15);

            migrationBuilder.DeleteData(
                table: "user_groups_extra_data",
                keyColumn: "group_id",
                keyValue: 16);

            migrationBuilder.DeleteData(
                table: "user_groups_extra_data",
                keyColumn: "group_id",
                keyValue: 17);

            migrationBuilder.DeleteData(
                table: "user_groups",
                keyColumn: "id",
                keyValue: 15);

            migrationBuilder.DeleteData(
                table: "user_groups",
                keyColumn: "id",
                keyValue: 16);

            migrationBuilder.DeleteData(
                table: "user_groups",
                keyColumn: "id",
                keyValue: 17);

            migrationBuilder.DropColumn(
                name: "use_email_in_translation_credits",
                table: "users");
        }
    }
}
