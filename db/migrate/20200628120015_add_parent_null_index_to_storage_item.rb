class AddParentNullIndexToStorageItem < ActiveRecord::Migration[5.2]
  def change
    add_index :storage_items, :name, unique: true, where: "parent_id is null"
  end
end
