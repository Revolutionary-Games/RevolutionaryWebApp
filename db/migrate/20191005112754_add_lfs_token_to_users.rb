class AddLfsTokenToUsers < ActiveRecord::Migration[5.2]
  def change
    add_column :users, :lfs_token, :string
    add_index :users, :lfs_token
  end
end
