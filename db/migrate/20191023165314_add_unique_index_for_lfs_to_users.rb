# frozen_string_literal: true

class AddUniqueIndexForLfsToUsers < ActiveRecord::Migration[5.2]
  def change
    remove_index :users, :lfs_token
    add_index :users, :lfs_token, unique: true
  end
end
