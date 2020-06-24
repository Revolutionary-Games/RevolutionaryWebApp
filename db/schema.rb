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

ActiveRecord::Schema.define(version: 2020_06_24_153746) do

  # These are extensions that must be enabled in order to support this database
  enable_extension "plpgsql"

  create_table "debug_symbols", force: :cascade do |t|
    t.string "symbol_hash"
    t.string "path"
    t.datetime "created_at", null: false
    t.datetime "updated_at", null: false
    t.string "name"
    t.integer "size"
    t.index ["name", "symbol_hash"], name: "index_debug_symbols_on_name_and_symbol_hash", unique: true
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
    t.index ["api_token"], name: "index_users_on_api_token", unique: true
    t.index ["email"], name: "index_users_on_email", unique: true
    t.index ["lfs_token"], name: "index_users_on_lfs_token", unique: true
  end

  add_foreign_key "lfs_objects", "lfs_projects"
  add_foreign_key "project_git_files", "lfs_projects"
end
