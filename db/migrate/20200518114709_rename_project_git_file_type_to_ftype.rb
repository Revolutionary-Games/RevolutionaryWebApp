# frozen_string_literal: true

class RenameProjectGitFileTypeToFtype < ActiveRecord::Migration[5.2]
  def change
    rename_column :project_git_files, :type, :ftype
  end
end
