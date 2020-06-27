class CreateStorageFiles < ActiveRecord::Migration[5.2]
  def change
    create_table :storage_files do |t|
      t.string :storage_path
      t.integer :size
      t.boolean :allow_parentless, default: false
      t.boolean :uploading, default: true
      t.timestamp :upload_expires

      t.timestamps
    end
    add_index :storage_files, :storage_path, unique: true
    add_index :storage_files, :uploading
  end
end
