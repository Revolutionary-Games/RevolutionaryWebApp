# This file is auto-generated from the current state of the database. Instead
# of editing this file, please use the migrations feature of Active Record to
# incrementally modify your database, and then regenerate this schema definition.
#
# Note that this schema.rb definition is the authoritative source for your
# database schema. If you need to create the application database on another
# system, you should be using db:schema:load, not running all the migrations
# from scratch. The latter is a flawed and unsustainable approach (the more migrations
# you'll amass, the slower it'll run and the greater likelihood for issues).
#
# It's strongly recommended that you check this file into your version control system.

ActiveRecord::Schema.define(version: 2020_07_13_101200) do

  # These are extensions that must be enabled in order to support this database
  enable_extension "plpgsql"

  create_table "access_keys", force: :cascade do |t|
    t.string "description"
    t.datetime "last_used"
    t.string "key_code"
    t.integer "key_type"
    t.datetime "created_at", null: false
    t.datetime "updated_at", null: false
    t.index ["key_code"], name: "index_access_keys_on_key_code", unique: true
  end

  create_table "debug_symbols", force: :cascade do |t|
    t.string "symbol_hash"
    t.string "path"
    t.datetime "created_at", null: false
    t.datetime "updated_at", null: false
    t.string "name"
    t.integer "size"
    t.index ["name", "symbol_hash"], name: "index_debug_symbols_on_name_and_symbol_hash", unique: true
  end

  create_table "dehydrated_objects", force: :cascade do |t|
    t.string "sha3"
    t.bigint "storage_item_id"
    t.datetime "created_at", null: false
    t.datetime "updated_at", null: false
    t.index ["sha3"], name: "index_dehydrated_objects_on_sha3", unique: true
    t.index ["storage_item_id"], name: "index_dehydrated_objects_on_storage_item_id"
  end

  create_table "dehydrated_objects_dev_builds", id: false, force: :cascade do |t|
    t.bigint "dehydrated_object_id", null: false
    t.bigint "dev_build_id", null: false
    t.index ["dehydrated_object_id", "dev_build_id"], name: "dehydrated_objects_dev_builds_index_compound", unique: true
  end

  create_table "dev_builds", force: :cascade do |t|
    t.string "build_hash"
    t.string "platform"
    t.string "branch"
    t.bigint "storage_item_id"
    t.boolean "verified", default: false
    t.boolean "anonymous"
    t.string "description"
    t.integer "score", default: 0
    t.integer "downloads", default: 0
    t.boolean "important", default: false
    t.boolean "keep", default: false
    t.string "pr_url"
    t.boolean "pr_fetched", default: false
    t.datetime "created_at", null: false
    t.datetime "updated_at", null: false
    t.string "build_zip_hash"
    t.index ["anonymous"], name: "index_dev_builds_on_anonymous"
    t.index ["build_hash", "platform"], name: "index_dev_builds_on_build_hash_and_platform", unique: true
    t.index ["storage_item_id"], name: "index_dev_builds_on_storage_item_id"
  end

  create_table "hyperstack_connections", force: :cascade do |t|
    t.string "channel"
    t.string "session"
    t.datetime "created_at"
    t.datetime "expires_at"
    t.datetime "refresh_at"
  end

  create_table "hyperstack_queued_messages", force: :cascade do |t|
    t.text "data"
    t.integer "connection_id"
  end

  create_table "launcher_links", force: :cascade do |t|
    t.string "link_code"
    t.string "last_ip"
    t.datetime "last_connection"
    t.integer "total_api_calls"
    t.bigint "user_id"
    t.datetime "created_at", null: false
    t.datetime "updated_at", null: false
    t.index ["link_code"], name: "index_launcher_links_on_link_code", unique: true
    t.index ["user_id"], name: "index_launcher_links_on_user_id"
  end

  create_table "lfs_objects", force: :cascade do |t|
    t.string "oid"
    t.integer "size"
    t.string "storage_path"
    t.bigint "lfs_project_id"
    t.datetime "created_at", null: false
    t.datetime "updated_at", null: false
    t.index ["lfs_project_id", "oid"], name: "index_lfs_objects_on_lfs_project_id_and_oid", unique: true
    t.index ["lfs_project_id"], name: "index_lfs_objects_on_lfs_project_id"
  end

  create_table "lfs_projects", force: :cascade do |t|
    t.string "name"
    t.string "slug"
    t.boolean "public"
    t.datetime "created_at", null: false
    t.datetime "updated_at", null: false
    t.integer "total_object_size"
    t.integer "total_object_count"
    t.datetime "total_size_updated"
    t.datetime "file_tree_updated"
    t.string "repo_url"
    t.string "clone_url"
    t.string "file_tree_commit"
    t.index ["name"], name: "index_lfs_projects_on_name", unique: true
    t.index ["slug"], name: "index_lfs_projects_on_slug", unique: true
  end

  create_table "patreon_settings", force: :cascade do |t|
    t.boolean "active"
    t.string "creator_token"
    t.string "creator_refresh_token"
    t.string "campaign_id"
    t.string "webhook_secret"
    t.integer "devbuilds_pledge_cents"
    t.integer "vip_pledge_cents"
    t.datetime "last_refreshed"
    t.datetime "last_webhook"
    t.datetime "created_at", null: false
    t.datetime "updated_at", null: false
    t.string "webhook_id"
    t.index ["webhook_id"], name: "index_patreon_settings_on_webhook_id", unique: true
  end

  create_table "patrons", force: :cascade do |t|
    t.boolean "suspended"
    t.string "username"
    t.string "email"
    t.integer "pledge_amount_cents"
    t.string "email_alias"
    t.string "patreon_token"
    t.string "patreon_refresh_token"
    t.datetime "created_at", null: false
    t.datetime "updated_at", null: false
    t.boolean "marked"
    t.boolean "has_forum_account"
    t.string "suspended_reason"
    t.index ["email"], name: "index_patrons_on_email", unique: true
    t.index ["email_alias"], name: "index_patrons_on_email_alias", unique: true
  end

  create_table "project_git_files", force: :cascade do |t|
    t.string "name"
    t.string "path"
    t.integer "size"
    t.string "ftype"
    t.string "lfs_oid"
    t.bigint "lfs_project_id"
    t.datetime "created_at", null: false
    t.datetime "updated_at", null: false
    t.index ["lfs_project_id"], name: "index_project_git_files_on_lfs_project_id"
  end

  create_table "reports", force: :cascade do |t|
    t.string "description"
    t.string "notes"
    t.string "extra_description"
    t.datetime "crash_time"
    t.string "reporter_ip"
    t.string "reporter_email"
    t.boolean "public"
    t.string "processed_dump"
    t.string "primary_callstack"
    t.string "log_files"
    t.string "delete_key"
    t.boolean "solved"
    t.string "solved_comment"
    t.bigint "duplicate_of_id"
    t.datetime "created_at", null: false
    t.datetime "updated_at", null: false
    t.string "game_version"
    t.index ["duplicate_of_id"], name: "index_reports_on_duplicate_of_id"
  end

  create_table "sessions", force: :cascade do |t|
    t.string "session_id", null: false
    t.text "data"
    t.datetime "created_at", null: false
    t.datetime "updated_at", null: false
    t.index ["session_id"], name: "index_sessions_on_session_id", unique: true
    t.index ["updated_at"], name: "index_sessions_on_updated_at"
  end

  create_table "storage_files", force: :cascade do |t|
    t.string "storage_path"
    t.integer "size"
    t.boolean "allow_parentless", default: false
    t.boolean "uploading", default: true
    t.datetime "upload_expires"
    t.datetime "created_at", null: false
    t.datetime "updated_at", null: false
    t.index ["storage_path"], name: "index_storage_files_on_storage_path", unique: true
    t.index ["uploading"], name: "index_storage_files_on_uploading"
  end

  create_table "storage_item_versions", force: :cascade do |t|
    t.integer "version", default: 1
    t.bigint "storage_item_id"
    t.bigint "storage_file_id"
    t.boolean "keep", default: false
    t.boolean "protected", default: false
    t.datetime "created_at", null: false
    t.datetime "updated_at", null: false
    t.boolean "uploading", default: true
    t.index ["storage_file_id"], name: "index_storage_item_versions_on_storage_file_id"
    t.index ["storage_item_id", "version"], name: "index_storage_item_versions_on_storage_item_id_and_version", unique: true
    t.index ["storage_item_id"], name: "index_storage_item_versions_on_storage_item_id"
  end

  create_table "storage_items", force: :cascade do |t|
    t.string "name"
    t.integer "ftype"
    t.boolean "special", default: false
    t.boolean "allow_parentless", default: false
    t.integer "size"
    t.integer "read_access", default: 2
    t.integer "write_access", default: 2
    t.bigint "owner_id"
    t.bigint "parent_id"
    t.datetime "created_at", null: false
    t.datetime "updated_at", null: false
    t.index ["allow_parentless"], name: "index_storage_items_on_allow_parentless"
    t.index ["name"], name: "index_storage_items_on_name", unique: true, where: "(parent_id IS NULL)"
    t.index ["owner_id"], name: "index_storage_items_on_owner_id"
    t.index ["parent_id", "name"], name: "index_storage_items_on_parent_id_and_name", unique: true
    t.index ["parent_id"], name: "index_storage_items_on_parent_id"
  end

  create_table "users", force: :cascade do |t|
    t.string "email"
    t.boolean "local"
    t.string "name"
    t.string "sso_source"
    t.boolean "developer"
    t.boolean "admin"
    t.string "password_digest"
    t.datetime "created_at", null: false
    t.datetime "updated_at", null: false
    t.string "api_token"
    t.string "lfs_token"
    t.boolean "suspended", default: false
    t.string "suspended_reason"
    t.boolean "suspended_manually", default: false
    t.integer "session_version", default: 1
    t.integer "total_launcher_links", default: 0
    t.string "launcher_link_code"
    t.datetime "launcher_code_expires"
    t.index ["api_token"], name: "index_users_on_api_token", unique: true
    t.index ["email"], name: "index_users_on_email", unique: true
    t.index ["launcher_link_code"], name: "index_users_on_launcher_link_code", unique: true
    t.index ["lfs_token"], name: "index_users_on_lfs_token", unique: true
  end

  add_foreign_key "dehydrated_objects", "storage_items"
  add_foreign_key "dev_builds", "storage_items"
  add_foreign_key "launcher_links", "users"
  add_foreign_key "lfs_objects", "lfs_projects"
  add_foreign_key "project_git_files", "lfs_projects"
  add_foreign_key "storage_item_versions", "storage_files"
  add_foreign_key "storage_item_versions", "storage_items"
  add_foreign_key "storage_items", "storage_items", column: "parent_id"
  add_foreign_key "storage_items", "users", column: "owner_id"
end
