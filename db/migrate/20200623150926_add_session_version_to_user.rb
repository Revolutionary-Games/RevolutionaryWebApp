class AddSessionVersionToUser < ActiveRecord::Migration[5.2]
  def change
    add_column :users, :session_version, :integer, default: 1
  end
end
