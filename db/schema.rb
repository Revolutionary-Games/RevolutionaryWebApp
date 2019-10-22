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

ActiveRecord::Schema.define(version: 2019_10_22_160607) do

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
    t.string "hash"
    t.integer "size"
    t.string "storage_path"
    t.bigint "lfs_project_id"
    t.datetime "created_at", null: false
    t.datetime "updated_at", null: false
    t.index ["lfs_project_id"], name: "index_lfs_objects_on_lfs_project_id"
  end

  create_table "lfs_projects", force: :cascade do |t|
    t.string "name"
    t.string "slug"
    t.boolean "public"
    t.datetime "created_at", null: false
    t.datetime "updated_at", null: false
    t.index ["name"], name: "index_lfs_projects_on_name", unique: true
    t.index ["slug"], name: "index_lfs_projects_on_slug", unique: true
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
    t.index ["api_token"], name: "index_users_on_api_token", unique: true
    t.index ["email"], name: "index_users_on_email", unique: true
    t.index ["lfs_token"], name: "index_users_on_lfs_token"
  end

  add_foreign_key "lfs_objects", "lfs_projects"
end
