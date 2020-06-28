# frozen_string_literal: true

class CreateStorageItemVersions < ActiveRecord::Migration[5.2]
  def change
    create_table :storage_item_versions do |t|
      t.integer :version, default: 1
      t.references :storage_item, foreign_key: true
      t.references :storage_file, foreign_key: true, index: true, unique: true
      t.boolean :keep, default: false
      t.boolean :protected, default: false

      t.timestamps
    end

    add_index :storage_item_versions, %i[storage_item_id version], unique: true
  end
end
