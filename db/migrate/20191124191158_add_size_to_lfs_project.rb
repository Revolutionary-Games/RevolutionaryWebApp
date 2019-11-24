class AddSizeToLfsProject < ActiveRecord::Migration[5.2]
  def change
    add_column :lfs_projects, :total_object_size, :integer
    add_column :lfs_projects, :total_object_count, :integer
    add_column :lfs_projects, :total_size_updated, :timestamp
    add_column :lfs_projects, :file_tree_updated, :timestamp
  end
end
