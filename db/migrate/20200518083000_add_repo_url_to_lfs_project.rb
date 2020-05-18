class AddRepoUrlToLfsProject < ActiveRecord::Migration[5.2]
  def change
    add_column :lfs_projects, :repo_url, :string
    add_column :lfs_projects, :clone_url, :string
  end
end
