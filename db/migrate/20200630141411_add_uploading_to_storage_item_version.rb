class AddUploadingToStorageItemVersion < ActiveRecord::Migration[5.2]
  def change
    add_column :storage_item_versions, :uploading, :boolean, default: true
  end
end
