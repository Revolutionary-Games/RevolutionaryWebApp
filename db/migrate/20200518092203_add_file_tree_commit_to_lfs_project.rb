class AddFileTreeCommitToLfsProject < ActiveRecord::Migration[5.2]
  def change
    add_column :lfs_projects, :file_tree_commit, :string
  end
end
