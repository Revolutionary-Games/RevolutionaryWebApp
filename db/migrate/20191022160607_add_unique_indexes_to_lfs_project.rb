# frozen_string_literal: true

class AddUniqueIndexesToLfsProject < ActiveRecord::Migration[5.2]
  def change
    add_index :lfs_projects, :name, unique: true
    add_index :lfs_projects, :slug, unique: true
  end
end
