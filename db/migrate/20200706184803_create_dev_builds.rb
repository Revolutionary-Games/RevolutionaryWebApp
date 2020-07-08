class CreateDevBuilds < ActiveRecord::Migration[5.2]
  def change
    create_table :dev_builds do |t|
      t.string :build_hash
      t.string :platform
      t.string :branch
      t.references :storage_item, foreign_key: true
      t.boolean :verified, default: false
      t.references :user, foreign_key: true
      t.boolean :anonymous
      t.string :description
      t.integer :score, default: 0
      t.integer :downloads, default: 0
      t.boolean :important, default: false
      t.boolean :keep, default: false
      t.string :pr_url
      t.boolean :pr_fetched, default: false
      t.boolean :build_of_the_day, default: false

      t.timestamps
    end
    add_index :dev_builds, [:build_hash, :platform], unique: true
    add_index :dev_builds, :anonymous
    add_index :dev_builds, :build_of_the_day
  end
end
