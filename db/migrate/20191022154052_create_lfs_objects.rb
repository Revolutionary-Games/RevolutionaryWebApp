class CreateLfsObjects < ActiveRecord::Migration[5.2]
  def change
    create_table :lfs_objects do |t|
      t.string :hash
      t.integer :size
      t.string :storage_path
      t.belongs_to :lfs_project, foreign_key: true

      t.timestamps
    end
  end
end
