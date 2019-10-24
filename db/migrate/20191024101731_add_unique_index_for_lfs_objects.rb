# frozen_string_literal: true

class AddUniqueIndexForLfsObjects < ActiveRecord::Migration[5.2]
  def change
    add_index :lfs_objects, %i[lfs_project_id hash], unique: true
  end
end
