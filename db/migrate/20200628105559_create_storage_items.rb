# frozen_string_literal: true

class CreateStorageItems < ActiveRecord::Migration[5.2]
  def change
    create_table :storage_items do |t|
      t.string :name
      t.integer :ftype
      t.boolean :special, default: false
      t.boolean :allow_parentless, default: false, index: true
      t.integer :size
      t.integer :read_access, default: 2
      t.integer :write_access, default: 2
      t.references :owner, foreign_key: { to_table: :users }
      t.references :parent, foreign_key: { to_table: :storage_items }

      t.timestamps
    end

    add_index :storage_items, %i[parent_id name], unique: true
  end
end
