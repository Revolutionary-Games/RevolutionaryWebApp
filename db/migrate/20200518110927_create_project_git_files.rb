# frozen_string_literal: true

class CreateProjectGitFiles < ActiveRecord::Migration[5.2]
  def change
    create_table :project_git_files do |t|
      t.string :name
      t.string :path
      t.integer :size
      t.string :type
      t.string :lfs_oid
      t.references :lfs_project, foreign_key: true

      t.timestamps
    end
  end
end
